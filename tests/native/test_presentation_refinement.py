"""Presentation source contracts only; these do not establish live WinUI behavior."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

APP = Path(__file__).resolve().parents[2] / "src/WinCare.App"
NS = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"


class PresentationRefinementTests(unittest.TestCase):
    def read(self, path):
        return (APP / path).read_text(encoding="utf-8-sig")

    def test_receipt_is_bound_before_technical_json_and_banner_remains_visible(self):
        page = self.read("Views/Pages/AllToolsPage.xaml")
        tree = ET.fromstring(page)
        receipt = next(e for e in tree.iter() if e.get("AutomationProperties.AutomationId") == "CommandPackageInventory")
        self.assertIn("Execution.PackageInventoryText", receipt.get("Text"))
        self.assertIn("Execution.HasPackageInventory", receipt.get("Visibility"))
        self.assertEqual("True", receipt.get("IsTextSelectionEnabled"))
        self.assertLess(page.index("CommandPackageInventory"), page.index("CommandResultDetails"))
        source = self.read("ViewModels/Pages/ToolExecutionViewModel.cs")
        result = source.split("private void ApplyExecutionResult", 1)[1].split("private void ClearExecutionResult", 1)[0]
        self.assertIn("IsExecutionResultOpen = true;", result)

    def test_catalog_reuse_checks_definitions_and_guards_list_feedback(self):
        source = self.read("ViewModels/Pages/AllToolsPageViewModel.cs")
        self.assertIn("existing.Definition == command", source)
        self.assertIn("? existing : new ToolRowViewModel(command)", source)
        self.assertIn("if (_isRefreshingTools || ReferenceEquals(_selectedTool, value)) return;", source)
        self.assertIn("finally { _isRefreshingTools = false; }", source)
        self.assertIn("_catalogSnapshot.SequenceEqual(snapshot)", source)
        self.assertNotIn("CatalogSignature", source)
        execution = self.read("ViewModels/Pages/ToolExecutionViewModel.cs")
        selection = execution.split("public void SelectTool", 1)[1].split("public void ApplyParameterValues", 1)[0]
        self.assertIn("ResetReviewState();", selection)

    def test_both_list_import_paths_distinguish_array_from_plain_string(self):
        source = self.read("ViewModels/Pages/ToolExecutionViewModel.cs")
        for start, end in [("public void ApplyParameterValues", "private void NotifyParameterValuesChanged"),
                           ("private bool TryImportAdvancedValues", "private static TimeSpan ExecutionBudget")]:
            block = source.split(start, 1)[1].split(end, 1)[0]
            self.assertIn("JsonValueKind.String => value.GetString()", block)
            self.assertIn("JsonValueKind.Array when field.Kind == CommandParameterKind.StringList", block)
            self.assertIn("string.Join(Environment.NewLine", block)

    def test_theme_refresh_invalidates_before_resolving_live_brushes(self):
        source = self.read("Converters/ThemeResourceBrushConverter.cs")
        refresh = source.split("public static void RefreshBrushes()", 1)[1].split("private static", 1)[0]
        self.assertLess(refresh.index("ResolvedColors.Clear();"), refresh.index("ResolveColor(key)"))

    def test_escape_bubbles_from_search_and_list_not_only_inspector(self):
        tree = ET.fromstring(self.read("Views/Pages/AllToolsPage.xaml"))
        self.assertEqual("Inspector_KeyDown", tree.find(NS + "Grid").get("KeyDown"))
        self.assertFalse(any(e.get("KeyDown") for e in tree.iter(NS + "Border")))
        self.assertIn("|| !ViewModel.IsDetailsOpen", self.read("Views/Pages/AllToolsPage.xaml.cs"))

    def test_confirmed_ui_failure_boundaries_have_guards(self):
        source = self.read("Views/Pages/PluginStorePage.xaml.cs")
        uninstall = source.split("private async void UninstallButton_Click", 1)[1].split("private async Task", 1)[0]
        self.assertLess(uninstall.index("try"), uninstall.index("new ContentDialog"))
        self.assertIn("catch (Exception ex)", uninstall)
        self.assertIn("ViewModel.ErrorMessage =", uninstall)
        help_source = self.read("Views/Pages/HelpPage.xaml.cs")
        self.assertIn("catch (Exception ex)", help_source)
        self.assertIn("TourError.IsOpen = true;", help_source)
        ET.fromstring(self.read("Views/Pages/HelpPage.xaml"))
        ai = self.read("Views/Pages/AiDoctorPage.xaml.cs").split("private void ScrollToLatestMessage()", 1)[1]
        self.assertLess(ai.index("try"), ai.index("ChangeView"))
        self.assertIn("catch (Exception ex)", ai)


if __name__ == "__main__":
    unittest.main()
