from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

import validate_powershell_calls


class PowerShellCallValidatorTests(unittest.TestCase):
    def _write(self, root: Path, relative: str, content: str) -> None:
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def test_scans_core_and_provider_calls(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self._write(
                root,
                "src/WinCare/Core/01-Test.ps1",
                "function Invoke-WinCareTarget { param([string]$Value) }\n",
            )
            self._write(
                root,
                "src/WinCare/Providers/10-Test.ps1",
                "Invoke-WinCareTarget -Wrong 'value'\n",
            )

            report = validate_powershell_calls.validate(root)

            self.assertEqual("failed", report["status"])
            self.assertTrue(
                any(
                    item.get("path") == "src/WinCare/Providers/10-Test.ps1"
                    and item.get("unsupportedParameters") == ["Wrong"]
                    for item in report["errors"]
                ),
                report["errors"],
            )

    def test_rejects_duplicate_named_parameters_in_one_call(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self._write(
                root,
                "src/WinCare/Core/01-Test.ps1",
                "function Invoke-WinCareTarget { param([string]$Value) }\n",
            )
            self._write(
                root,
                "src/WinCare/Providers/10-Test.ps1",
                "Invoke-WinCareTarget `\n  -Value 'first' `\n  -Value 'second'\n",
            )

            report = validate_powershell_calls.validate(root)

            self.assertEqual("failed", report["status"])
            self.assertTrue(
                any(
                    item.get("duplicateParameters") == ["Value"]
                    for item in report["errors"]
                ),
                report["errors"],
            )


if __name__ == "__main__":
    unittest.main()
