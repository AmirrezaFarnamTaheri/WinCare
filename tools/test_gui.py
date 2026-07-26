from __future__ import annotations

import importlib.util
import json
import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("validate_gui", ROOT / "tools/validate_gui.py")
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class GuiValidationTests(unittest.TestCase):
    def test_primary_contrast_exceeds_wcag_aa(self) -> None:
        self.assertGreaterEqual(MODULE.contrast("#F4F7FB", "#0B1020"), 4.5)
        self.assertGreaterEqual(MODULE.contrast("#F97066", "#512128"), 4.5)

    def test_gui_json_ids_are_unique(self) -> None:
        for name in ("actions.json", "navigation.json"):
            data = json.loads((ROOT / "src/WinCare/Data/Gui" / name).read_text("utf-8"))
            identifiers = [item["id"] for item in data]
            self.assertEqual(len(identifiers), len(set(identifiers)))

    def test_xaml_is_code_behind_free_and_named(self) -> None:
        namespace = "{http://schemas.microsoft.com/winfx/2006/xaml}"
        tree = ET.parse(ROOT / "src/WinCare/Data/Gui/WinCare.MainWindow.xaml")
        names = [value for element in tree.iter() for key, value in element.attrib.items() if key == namespace + "Name"]
        self.assertIn("MainWindow", names)
        self.assertIn("ConfirmOverlay", names)
        self.assertIn("AssurancePanel", names)
        self.assertFalse(any(namespace + "Class" in element.attrib for element in tree.iter()))

    def test_gui_process_boundary_is_argument_safe_and_bounded(self) -> None:
        runtime = (ROOT / "src/WinCare/UI/Gui/10-GuiRuntime.ps1").read_text("utf-8")
        self.assertIn("[Diagnostics.ProcessStartInfo]::new()", runtime)
        self.assertIn("$psi.ArgumentList.Add", runtime)
        self.assertIn("$psi.WorkingDirectory=$root", runtime)
        self.assertIn("24576 bytes", runtime)
        self.assertNotRegex(runtime, r"Invoke-WinCarePlan\s+-Plan")

    def test_gui_themes_are_dynamic_and_accessible(self) -> None:
        xaml = (ROOT / "src/WinCare/Data/Gui/WinCare.MainWindow.xaml").read_text("utf-8")
        runtime = (ROOT / "src/WinCare/UI/Gui/10-GuiRuntime.ps1").read_text("utf-8")
        for token in ("BackgroundBrush", "SurfaceBrush", "AccentBrush", "TextBrush"):
            self.assertIn("{DynamicResource " + token + "}", xaml)
        self.assertIn("ValidateSet('Normal','HighContrast','Monochrome')", runtime)
        self.assertIn("[Windows.SystemParameters]::HighContrast", runtime)

    def test_assurance_panel_has_no_research_history_dependency(self) -> None:
        xaml = (ROOT / "src/WinCare/Data/Gui/WinCare.MainWindow.xaml").read_text("utf-8")
        runtime = (ROOT / "src/WinCare/UI/Gui/10-GuiRuntime.ps1").read_text("utf-8")
        navigation = json.loads((ROOT / "src/WinCare/Data/Gui/navigation.json").read_text("utf-8"))
        self.assertIn('x:Name="AssurancePanel"', xaml)
        self.assertIn("Update-WinCareGuiAssurance", runtime)
        self.assertIn("assurance", {item["id"] for item in navigation})

    def test_xaml_resource_references_are_resolved(self) -> None:
        text = (ROOT / "src/WinCare/Data/Gui/WinCare.MainWindow.xaml").read_text("utf-8")
        definitions = set(re.findall(r'x:Key\s*=\s*["\']([^"\']+)["\']', text))
        references = set(re.findall(r"\{(?:StaticResource|DynamicResource)\s+([^},\s]+)", text))
        self.assertEqual(references - definitions, set())

    def test_pester_inventory_and_gui_suite_are_complete(self) -> None:
        suites = sorted((ROOT / "tests").glob("*.ps1"))
        counts = {
            path.name: len(re.findall(r"^\s*It\s+['\"]", path.read_text("utf-8"), re.M))
            for path in suites
        }
        self.assertGreaterEqual(len(suites), 15)
        self.assertGreaterEqual(sum(counts.values()), 190)
        self.assertGreaterEqual(counts["Gui.Contracts.Tests.ps1"], 10)


if __name__ == "__main__":
    unittest.main()
