from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class ProductUiParityTests(unittest.TestCase):
    def read(self, relative: str) -> str:
        return (ROOT / relative).read_text(encoding="utf-8")

    def test_home_is_presentation_only_and_compact_layout_has_real_rows(self) -> None:
        view_model = self.read("src/WinCare.App/ViewModels/Pages/HomePageViewModel.cs")
        xaml = self.read("src/WinCare.App/Views/Pages/HomePage.xaml")
        code_behind = self.read("src/WinCare.App/Views/Pages/HomePage.xaml.cs")

        for stale_owner in (
            "CommandDispatcher",
            "INativeSystemProbeRepository",
            "QuickCleanCommand",
            "StartupBoostCommand",
            "NetworkRefreshCommand",
            "ToggleInspectorCommand",
        ):
            self.assertNotIn(stale_owner, view_model)
        self.assertIn("Presentation-only projection", view_model)
        self.assertIn('x:Name="WelcomeLayout"', xaml)
        self.assertIn('x:Name="HeroLayout"', xaml)
        self.assertGreaterEqual(xaml.count('<RowDefinition Height="Auto"/>'), 4)
        self.assertIn("PageNavigation.NavigateToSection", code_behind)

    def test_troubleshoot_hands_actions_to_canonical_tool_inspector(self) -> None:
        view_model = self.read("src/WinCare.App/ViewModels/Pages/AiDoctorPageViewModel.cs")
        code_behind = self.read("src/WinCare.App/Views/Pages/AiDoctorPage.xaml.cs")
        xaml = self.read("src/WinCare.App/Views/Pages/AiDoctorPage.xaml")

        self.assertNotIn("ICommandDispatcher", view_model)
        self.assertNotIn("PreviewStepAsync", view_model)
        self.assertNotIn("ApplyPreviewedStepAsync", view_model)
        self.assertNotIn("ContentDialog", code_behind)
        self.assertIn("PageNavigation.OpenTool", code_behind)
        self.assertIn("normal preview, approval, execution, and activity flow", xaml)

    def test_power_tools_uses_named_controls_instead_of_visual_tree_order(self) -> None:
        xaml = self.read("src/WinCare.App/Views/Pages/AllToolsPage.xaml")
        code_behind = self.read("src/WinCare.App/Views/Pages/AllToolsPage.xaml.cs")

        for name in ("AreaFilter", "SectionFilter", "RiskFilter", "ReadOnlyFilter", "ResultCountText", "ParameterExpander"):
            self.assertIn(f'x:Name="{name}"', xaml)
        self.assertIn('AutomationProperties.AutomationId="ExecuteSelectedTool"', xaml)
        self.assertIn('AutomationProperties.AutomationId="CommandResultDetails"', xaml)
        self.assertNotIn("FindVisualDescendant", code_behind)
        self.assertNotIn("OfType<ComboBox>", code_behind)
        self.assertNotIn("ToolSearchBox.Parent as Grid", code_behind)

    def test_extension_catalog_trust_state_is_visible(self) -> None:
        view_model = self.read("src/WinCare.App/ViewModels/Pages/PluginStorePageViewModel.cs")
        xaml = self.read("src/WinCare.App/Views/Pages/PluginStorePage.xaml")

        self.assertIn("CatalogStatusMessage", view_model)
        self.assertIn("IsCatalogTrustVerified", view_model)
        self.assertIn("ViewModel.CatalogStatusMessage", xaml)
        self.assertIn('AutomationProperties.AutomationId="PluginCatalogStatus"', xaml)

    def test_checkup_keeps_follow_up_actions_and_uses_stable_section_names(self) -> None:
        view_model = self.read("src/WinCare.App/ViewModels/Pages/CheckupPageViewModel.cs")
        page = self.read("src/WinCare.App/Views/Pages/CheckupPage.xaml.cs")

        self.assertIn("ApplyUpdateOutcome(row", view_model)
        self.assertIn("ApplyUpdateOutcome(resultRow", view_model)
        self.assertIn('SetNavigationAction(row, "Review updates", "system-care", "Network & updates")', view_model)
        self.assertGreaterEqual(view_model.count('SetNavigationAction(securityRow, "Review security", "security", "Status")'), 2)
        self.assertIn("NavigationSectionTitle", view_model)
        self.assertIn("PageNavigation.NavigateToSection", page)


if __name__ == "__main__":
    unittest.main()
