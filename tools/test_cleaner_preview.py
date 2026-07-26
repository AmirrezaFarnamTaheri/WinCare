from __future__ import annotations

import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


class CleanerPreviewTests(unittest.TestCase):
    def test_routes_contracts_and_dispatch_are_complete(self) -> None:
        headless = (ROOT / "src/WinCare/UI/98-Headless.ps1").read_text("utf-8")
        contracts = (ROOT / "src/WinCare/Core/11-ActionContracts.ps1").read_text("utf-8")
        dispatch = (ROOT / "src/WinCare/Core/09-Transactions.ps1").read_text("utf-8")
        for name in ("cleaner-disk-pressure", "cleaner-winapp2-run", "cleaner-relocation", "file-preview", "preview-handlers"):
            self.assertIn("'" + name + "'", headless)
        for name in ("DeleteAdmittedCleanupFiles", "RelocateDirectoryWithJunction", "RestoreRelocatedDirectory"):
            self.assertIn("Add-Contract '" + name + "'", contracts)
            self.assertRegex(dispatch, re.escape("'" + name + "'{Invoke-WinCare"))

    def test_provider_preserves_guardrails(self) -> None:
        text = (ROOT / "src/WinCare/Providers/111-CleanerPreviewStudio.ps1").read_text("utf-8")
        for phrase in (
            "Drive-root wildcard scan", "Defender history deletion", "Restore-point deletion",
            "Remote UNC paths are not admitted", "DtdProcessing]::Prohibit", "Loaded=$false",
        ):
            self.assertIn(phrase, text)
        self.assertNotRegex(text, r"Invoke-Expression|ScriptBlock::Create|Stop-Process|SetWindowsHookEx")

    def test_gui_actions_are_unique_and_refer_to_routes(self) -> None:
        actions = json.loads((ROOT / "src/WinCare/Data/Gui/actions.json").read_text("utf-8"))
        identifiers = [action["id"] for action in actions]
        self.assertEqual(len(identifiers), len(set(identifiers)))
        selected = {
            action["id"]: action
            for action in actions
            if action["id"] in {"disk-pressure-cleanup", "winapp2-assessment", "local-file-preview", "preview-handler-assessment"}
        }
        self.assertEqual(len(selected), 4)
        headless = (ROOT / "src/WinCare/UI/98-Headless.ps1").read_text("utf-8")
        for action in selected.values():
            self.assertIn("'" + action["command"] + "'", headless)


if __name__ == "__main__":
    unittest.main()
