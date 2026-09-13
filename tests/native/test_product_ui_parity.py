from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class ProductUiParityTests(unittest.TestCase):
    def read(self, relative: str) -> str:
        return (ROOT / relative).read_text(encoding="utf-8")

    def test_home_is_presentation_only_and_has_one_primary_checkup_cta(self) -> None:
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
        self.assertEqual(1, xaml.count('Content="Run checkup"'))
        self.assertIn('x:Name="HeroLayout"', xaml)
        self.assertIn("Grid.SetRow(EvidenceSummaryCard", code_behind)
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
        self.assertIn("normal risk-tier review, execution, and Activity flow", xaml)

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
        dialog = self.read("src/WinCare.App/Views/Dialogs/PluginDetailDialog.xaml")

        self.assertIn("CatalogStatusMessage", view_model)
        self.assertIn("IsCatalogTrustVerified", view_model)
        self.assertIn("ViewModel.CatalogStatusMessage", xaml)
        self.assertIn('AutomationProperties.AutomationId="PluginCatalogStatus"', xaml)
        self.assertIn("current catalog and package trust checks pass", dialog)

    def test_checkup_keeps_follow_up_actions_and_uses_stable_section_names(self) -> None:
        view_model = self.read("src/WinCare.App/ViewModels/Pages/CheckupPageViewModel.cs")
        page = self.read("src/WinCare.App/Views/Pages/CheckupPage.xaml.cs")

        self.assertIn("ApplyUpdateOutcome(row", view_model)
        self.assertIn("ApplyUpdateOutcome(resultRow", view_model)
        self.assertIn('SetNavigationAction(row, "Review updates", "system-care", "Network & updates")', view_model)
        self.assertGreaterEqual(view_model.count('SetNavigationAction(securityRow, "Review security", "security", "Status")'), 2)
        self.assertIn("NavigationSectionTitle", view_model)
        self.assertIn("PageNavigation.NavigateToSection", page)

    def test_legacy_instrument_panel_styles_are_removed(self) -> None:
        controls = self.read("src/WinCare.App/Styles/ControlStyles.xaml")
        theme = self.read("src/WinCare.App/Styles/ThemeResources.xaml")
        for legacy in ("DoubleBezel", "HudChassis", "LuminousGlow", "IslandIcon", "TelemetrySensorBox", "EyebrowBadge"):
            self.assertNotIn(legacy, controls)
            self.assertNotIn(legacy, theme)

    def test_product_documentation_matches_current_execution_and_capture_contracts(self) -> None:
        guide = self.read("docs/User-Guide.md")
        screenshots = self.read("docs/Screenshots.md")
        readme = self.read("README.md")

        self.assertNotIn("confirmation dialog", guide)
        self.assertNotIn("probes run sequentially", guide)
        self.assertIn("fast system/storage/security probes run concurrently", guide)
        self.assertIn("Power tools applies the normal risk-tier flow", guide)
        self.assertIn("historical runtime evidence", screenshots)
        self.assertIn("Historical v2.5.0-rc5 runtime capture", readme)


if __name__ == "__main__":
    unittest.main()
