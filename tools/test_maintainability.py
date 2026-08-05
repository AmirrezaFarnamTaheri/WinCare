from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from tools import validate_maintainability as vm


class MaintainabilityValidationTests(unittest.TestCase):
    def make_repo(self, root: Path) -> None:
        module = root / "src/WinCare"
        for folder in ("Core", "Providers", "UI", "UI/Gui", "UI/Screens", "SourceManifests"):
            (module / folder).mkdir(parents=True, exist_ok=True)
        (module / "Core/Test.ps1").write_text(
            "function Get-Test {\n    return 1\n}\n",
            encoding="utf-8",
        )
        manifests = {
            "Core.psd1": ("Core", ["Core/Test.ps1"]),
            "Providers.psd1": ("Providers", []),
            "UI.psd1": ("UI", []),
        }
        for name, (group, files) in manifests.items():
            body = "@{\n    SchemaVersion = 1\n    Group = '%s'\n    Files = @(\n%s    )\n}\n" % (
                group,
                "".join(f"        '{item}'\n" for item in files),
            )
            (module / "SourceManifests" / name).write_text(body, encoding="utf-8")

    def test_baseline_passes_and_density_regression_fails(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.make_repo(root)
            baseline_path = root / "config/maintainability-baseline.json"
            baseline_path.parent.mkdir(parents=True)
            document = vm.baseline_document(vm.collect(root))
            baseline_path.write_text(json.dumps(document), encoding="utf-8")
            self.assertEqual("passed", vm.validate(root, baseline_path)["status"])
            with (root / "src/WinCare/Core/Test.ps1").open("a", encoding="utf-8") as stream:
                stream.write("# " + ("x" * 250) + "\n")
            report = vm.validate(root, baseline_path)
            self.assertEqual("failed", report["status"])
            self.assertTrue(
                any("maxLineLength" in item or "linesOver200" in item for item in report["errors"])
            )

    def test_strict_file_allows_readable_growth_but_rejects_long_lines(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.make_repo(root)
            baseline_path = root / "config/maintainability-baseline.json"
            baseline_path.parent.mkdir(parents=True)
            document = vm.baseline_document(vm.collect(root), strict_files=["Core/Test.ps1"])
            baseline_path.write_text(json.dumps(document), encoding="utf-8")
            (root / "src/WinCare/Core/Test.ps1").write_text(
                "function Get-Test {\n" + "    $value = 1\n" * 40 + "    return $value\n}\n",
                encoding="utf-8",
            )
            self.assertEqual("passed", vm.validate(root, baseline_path)["status"])
            with (root / "src/WinCare/Core/Test.ps1").open("a", encoding="utf-8") as stream:
                stream.write("# " + ("x" * 250) + "\n")
            self.assertEqual("failed", vm.validate(root, baseline_path)["status"])

    def test_ast_linter_detects_forbidden_invoke_and_nonstandard_naming(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.make_repo(root)
            baseline_path = root / "config/maintainability-baseline.json"
            baseline_path.parent.mkdir(parents=True)
            document = vm.baseline_document(vm.collect(root))
            baseline_path.write_text(json.dumps(document), encoding="utf-8")
            (root / "src/WinCare/Core/Test.ps1").write_text(
                "function invalidName {\n    Invoke-Expression 'Get-Date'\n}\n",
                encoding="utf-8",
            )
            report = vm.validate(root, baseline_path)
            self.assertEqual("failed", report["status"])
            self.assertTrue(any("Forbidden dynamic invocation" in err for err in report["errors"]))
            self.assertTrue(any("Non-standard function naming" in err for err in report["errors"]))


if __name__ == "__main__":
    unittest.main()
