from __future__ import annotations

import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from tools.validate_source_references import expected_reference_id, validate


class SourceReferenceValidationTests(unittest.TestCase):
    def _make_tree(self, source_text: str) -> tuple[tempfile.TemporaryDirectory[str], Path, str]:
        temporary = tempfile.TemporaryDirectory()
        root = Path(temporary.name)
        source_root = root / "src" / "WinCare"
        provider = source_root / "Providers" / "10-Test.ps1"
        index_path = source_root / "Data" / "SourceReferenceIndex.json"
        provider.parent.mkdir(parents=True)
        index_path.parent.mkdir(parents=True)
        identifier = expected_reference_id("Providers/10-Test.ps1")
        provider.write_text(source_text.replace("$REFERENCE", identifier), encoding="utf-8")
        payload = {
            "schema": "wincare.source-reference-index/v1",
            "references": [
                {
                    "id": identifier,
                    "path": "Providers/10-Test.ps1",
                    "sha256": hashlib.sha256(provider.read_bytes()).hexdigest(),
                }
            ],
        }
        index_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
        return temporary, root, identifier

    def test_expected_id_is_path_derived(self) -> None:
        self.assertEqual(expected_reference_id("Core/10-Catalog.ps1"), "SRC:4726046a03")

    def test_valid_reference_index_passes(self) -> None:
        temporary, root, identifier = self._make_tree("$x=@('$REFERENCE')\n")
        self.addCleanup(temporary.cleanup)
        report = validate(root)
        self.assertEqual(report["status"], "passed", report)
        self.assertEqual(report["usedReferences"], 1)
        self.assertEqual(identifier, report.get("errors", []) and "" or identifier)

    def test_hash_drift_is_rejected(self) -> None:
        temporary, root, _ = self._make_tree("$x=@('$REFERENCE')\n")
        self.addCleanup(temporary.cleanup)
        provider = root / "src" / "WinCare" / "Providers" / "10-Test.ps1"
        provider.write_text(provider.read_text(encoding="utf-8") + "$changed=$true\n", encoding="utf-8")
        report = validate(root)
        self.assertEqual(report["status"], "failed")
        self.assertTrue(any("hash drift" in error for error in report["errors"]))

    def test_legacy_reference_is_rejected(self) -> None:
        temporary, root, _ = self._make_tree("$x=@('$REFERENCE','D10-OLD-RECORD')\n")
        self.addCleanup(temporary.cleanup)
        report = validate(root)
        self.assertEqual(report["status"], "failed")
        self.assertTrue(any("Legacy source reference" in error for error in report["errors"]))

    def test_unindexed_reference_is_rejected(self) -> None:
        temporary, root, _ = self._make_tree("$x=@('$REFERENCE','SRC:aaaaaaaaaa')\n")
        self.addCleanup(temporary.cleanup)
        report = validate(root)
        self.assertEqual(report["status"], "failed")
        self.assertTrue(any("Unindexed source reference" in error for error in report["errors"]))


if __name__ == "__main__":
    unittest.main()
