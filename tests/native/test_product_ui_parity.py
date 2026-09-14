from __future__ import annotations

import re
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

        for stale_owner in ("CommandDispatcher", "INativeSystemProbeRepository", "QuickCleanCommand", "StartupBoostCommand", "NetworkRefreshCommand", "ToggleInspectorCommand"):
            self.assertNotIn(stale_owner, view_model)
        self.assertIn("Presentation-only projection", view_model)
        self.assertEqual(1, xaml.count('Content="Run checkup"'))
        self.assertIn('x:Name="HeroLayout"', xaml)
        self.assertIn("Grid.SetRow(EvidenceSummaryCard", code_behind)
        self.assertIn("PageNavigation.NavigateToSection", code_behind)
        self.assertIn('Text="Common care"', xaml)
        self.assertIn("Recent evidence is ready", view_model)
        self.assertIn("Some evidence is getting stale", view_model)
        self.assertIn('EvidenceScoreText = $"{collected} of {QuickCheckCommandIds.Length} areas";', view_model)
        self.assertIn("record.CompletedAt ?? record.StartedAt", view_model)
        self.assertNotIn("PerformanceStatus", view_model)
        self.assertNotIn("NavCategory_Performance_Click", xaml)
        self.assertNotIn("NavCategory_Performance_Click", code_behind)
        for evidence_handler in ("NavCategory_System_Click", "NavCategory_Storage_Click", "NavCategory_Security_Click", "NavCategory_Updates_Click"):
            self.assertIn(evidence_handler, xaml)
            self.assertIn(evidence_handler, code_behind)

    def test_troubleshoot_hands_actions_to_canonical_tool_inspector(self) -> None:
        view_model = self.read("src/WinCare.App/ViewModels/Pages/AiDoctorPageViewModel.cs")
        code_behind = self.read("src/WinCare.App/Views/Pages/AiDoctorPage.xaml.cs")
        xaml = self.read("src/WinCare.App/Views/Pages/AiDoctorPage.xaml")
        action_plan = self.read("src/WinCare.Application/Diagnostics/DoctorActionPlan.cs")
        translator = self.read("src/WinCare.Application/Diagnostics/IntentTranslator.cs")

        self.assertNotIn("ICommandDispatcher", view_model)
        self.assertNotIn("PreviewStepAsync", view_model)
        self.assertNotIn("ApplyPreviewedStepAsync", view_model)
        self.assertNotIn("ContentDialog", code_behind)
        self.assertIn("PageNavigation.OpenTool", code_behind)
        self.assertIn("normal risk-tier review, execution, and Activity flow", xaml)
        self.assertIn('AutomationProperties.Name="{x:Bind ActionAccessibleName}"', xaml)
        self.assertIn("AccessRequirement", action_plan)
        self.assertIn("AdministratorAccess.MayBeRequired", action_plan)
        self.assertIn("ActionAccessibleName", action_plan)
        self.assertIn("HasMutatingActions => ProposedSteps.Any(step => !step.IsReadOnly)", action_plan)
        self.assertIn("UndoAvailable: false", translator)
        self.assertIn("ReadOnly: match.ReadOnly", translator)
        self.assertIn("AccessRequirement: match.AdministratorAccess", translator)
        self.assertNotIn("UndoAvailable: match.Risk", translator)

    def test_care_rows_preserve_parameters_and_open_the_canonical_tool_inspector(self) -> None:
        control = self.read("src/WinCare.App/Controls/CareToolList.xaml.cs")
        xaml = self.read("src/WinCare.App/Controls/CareToolList.xaml")

        self.assertIn("PageNavigation.OpenTool", control)
        self.assertNotIn("PageNavigation.OpenTools", control)
        self.assertIn("CommandParameters is JsonElement", control)
        self.assertNotIn("review any change before it runs", xaml)
        self.assertIn("open it in Power tools", xaml)

    def test_power_tools_uses_named_controls_instead_of_visual_tree_order(self) -> None:
        xaml = self.read("src/WinCare.App/Views/Pages/AllToolsPage.xaml")
        code_behind = self.read("src/WinCare.App/Views/Pages/AllToolsPage.xaml.cs")
        view_model = self.read("src/WinCare.App/ViewModels/Pages/AllToolsPageViewModel.cs")

        for name in ("AreaFilter", "SectionFilter", "RiskFilter", "ReadOnlyFilter", "ResultCountText", "ParameterExpander"):
            self.assertIn(f'x:Name="{name}"', xaml)
        self.assertIn('AutomationProperties.AutomationId="ExecuteSelectedTool"', xaml)
        self.assertIn('AutomationProperties.AutomationId="CommandResultDetails"', xaml)
        self.assertIn("BoolToVisibility(ViewModel.Execution.IsExecuting)", xaml)
        self.assertIn("FavoriteActionLabel", xaml)
        self.assertIn("public bool OpenCategory", view_model)
        self.assertIn("bool categoryStillExists", view_model)
        self.assertIn("!ViewModel.OpenCategory", code_behind)
        self.assertNotIn("FindVisualDescendant", code_behind)
        self.assertNotIn("OfType<ComboBox>", code_behind)
        self.assertNotIn("ToolSearchBox.Parent as Grid", code_behind)

    def test_power_tools_uses_product_safety_tiers_instead_of_raw_catalog_risk(self) -> None:
        options = self.read("src/WinCare.App/ViewModels/Pages/FilterOption.cs")
        view_model = self.read("src/WinCare.App/ViewModels/Pages/AllToolsPageViewModel.cs")
        row = self.read("src/WinCare.App/ViewModels/Pages/ToolRowViewModel.cs")

        self.assertIn("RiskTier? Value", options)
        for label, tier in (("Safe", "Safe"), ("Moderate", "Moderate"), ("Destructive", "Destructive")):
            self.assertIn(f'new RiskFilterOption("{label}", RiskTier.{tier})', view_model)
        for leaked in ("CommandRisk.ReadOnly", "CommandRisk.Low", "CommandRisk.High", "CommandRisk.Critical"):
            self.assertNotIn(leaked, view_model)
        self.assertIn("command.RiskTier == tier", view_model)
        self.assertIn("Definition.RiskTier", row)

    def test_extension_catalog_trust_state_and_compact_layout_are_visible(self) -> None:
        view_model = self.read("src/WinCare.App/ViewModels/Pages/PluginStorePageViewModel.cs")
        card = self.read("src/WinCare.App/ViewModels/Pages/PluginCardViewModel.cs")
        xaml = self.read("src/WinCare.App/Views/Pages/PluginStorePage.xaml")
        dialog = self.read("src/WinCare.App/Views/Dialogs/PluginDetailDialog.xaml")
        code_behind = self.read("src/WinCare.App/Views/Pages/PluginStorePage.xaml.cs")

        self.assertIn("CatalogStatusMessage", view_model)
        self.assertIn("IsCatalogTrustVerified", view_model)
        self.assertIn("ViewModel.CatalogStatusMessage", xaml)
        self.assertIn('AutomationProperties.AutomationId="PluginCatalogStatus"', xaml)
        self.assertIn('Header="Catalog status"', xaml)
        self.assertNotIn('Title="Catalog trust and availability"', xaml)
        self.assertIn("current catalog and package trust checks pass", dialog)
        self.assertNotIn("plugin package and its registered commands", code_behind)
        self.assertIn('ColumnDefinitions="*,Auto" RowDefinitions="Auto,Auto,Auto"', xaml)
        self.assertIn("Grid.SetRow(CategoryFilter, compact ? 2 : 1)", code_behind)
        for property_name in ("DetailsAccessibleName", "InstallAccessibleName", "EnableAccessibleName", "DisableAccessibleName", "UninstallAccessibleName"):
            self.assertIn(property_name, card)
            self.assertIn(f'AutomationProperties.Name="{{x:Bind {property_name}}}"', xaml)
        self.assertNotIn('Text="{x:Bind StatusBadgeText}" FontFamily="{StaticResource TelemetryFontFamily}"', xaml)

    def test_checkup_keeps_follow_up_actions_and_uses_stable_section_names(self) -> None:
        view_model = self.read("src/WinCare.App/ViewModels/Pages/CheckupPageViewModel.cs")
        page = self.read("src/WinCare.App/Views/Pages/CheckupPage.xaml.cs")
        xaml = self.read("src/WinCare.App/Views/Pages/CheckupPage.xaml")

        self.assertIn("ApplyUpdateOutcome(row", view_model)
        self.assertIn("ApplyUpdateOutcome(resultRow", view_model)
        self.assertIn('SetNavigationAction(row, "Review updates", "system-care", "Network & updates")', view_model)
        self.assertGreaterEqual(view_model.count('SetNavigationAction(securityRow, "Review security", "security", "Status")'), 2)
        self.assertIn("NavigationSectionTitle", view_model)
        self.assertIn("PageNavigation.NavigateToSection", page)
        self.assertIn("new PageRow(quickRow.Title, quickRow.Description", view_model)
        self.assertNotIn("Background update query failed: {ex.Message}", view_model)
        self.assertIn('AutomationProperties.Name="{x:Bind ActionAccessibleName, Mode=OneWay}"', xaml)
        self.assertIn('Text="Checkup status"', xaml)
        self.assertNotIn('Text="CHECKUP STATUS"', xaml)

    def test_shell_page_service_and_navigation_catalog_share_one_route_set(self) -> None:
        catalog = self.read("src/WinCare.Application/Navigation/NavigationCatalog.cs")
        page_service = self.read("src/WinCare.App/Services/PageService.cs")
        shell = self.read("src/WinCare.App/Views/ShellPage.xaml")
        shell_code = self.read("src/WinCare.App/Views/ShellPage.xaml.cs")

        catalog_ids = set(re.findall(r'new\("([^"]+)"', catalog))
        page_ids = set(re.findall(r'\["([^"]+)"\]\s*=\s*typeof', page_service))
        shell_ids = set(re.findall(r'Tag="([^"]+)"', shell))
        self.assertEqual(catalog_ids, page_ids)
        self.assertEqual(catalog_ids - {"about"}, shell_ids)
        self.assertIn("PrimaryNavigation.SelectedItem = null", shell_code)

    def test_secondary_surfaces_do_not_repeat_policy_as_product_content(self) -> None:
        settings = self.read("src/WinCare.App/Views/Pages/SettingsPage.xaml")
        about = self.read("src/WinCare.App/Views/Pages/AboutPage.xaml")
        catalog = self.read("src/WinCare.Application/Navigation/NavigationCatalog.cs")

        self.assertNotIn('Text="Safety policy"', settings)
        self.assertNotIn('"Safety policy"', catalog)
        self.assertNotIn('Text="Safety model"', about)
        self.assertIn('Text="Open source"', about)
        self.assertIn('"Common care"', catalog)
        self.assertNotIn("not durable", settings.lower())

    def test_activity_copy_matches_terminal_history_semantics(self) -> None:
        activity = self.read("src/WinCare.App/Views/Pages/ActivityPage.xaml")
        view_model = self.read("src/WinCare.App/ViewModels/Pages/ActivityPageViewModel.cs")
        catalog = self.read("src/WinCare.Application/Navigation/NavigationCatalog.cs")

        self.assertNotIn("pending confirmations", activity)
        self.assertNotIn("elevated confirmation", activity)
        self.assertIn("operations ended in a state that needs review or follow-up", activity)
        self.assertIn("Recent operations leave a record here", activity)
        self.assertNotIn("Activity history is not durable", activity)
        self.assertNotIn('FontFamily="{StaticResource TelemetryFontFamily}"', activity)
        self.assertIn('Text="History"', activity)
        self.assertIn('new PageSection("History"', view_model)
        self.assertIn("ActivityState.Failed", view_model)
        self.assertIn("ActivityState.Cancelled", view_model)
        self.assertIn('["Running", "Needs attention", "History", "Reports"]', catalog)

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
        architecture = self.read("docs/Architecture.md")
        architecture_plain = architecture.replace("**", "")
        c4 = self.read("docs/architecture/c4-model.md")
        design = self.read("DESIGN.md")
        ux_contract = self.read("UX-CONTRACT.md")

        self.assertNotIn("confirmation dialog", guide)
        self.assertNotIn("probes run sequentially", guide)
        self.assertIn("fast system/storage/security probes run concurrently", guide)
        self.assertIn("Power tools applies the normal risk-tier flow", guide)
        self.assertIn("**History** — all terminal successful, failed, and cancelled operations", guide)
        self.assertNotIn("**Safety policy** —", guide)
        self.assertIn("historical runtime evidence", screenshots)
        self.assertIn("Historical v2.5.0-rc5 runtime capture", readme)
        self.assertIn("Power tools is the canonical advanced command inspector and execution surface", architecture_plain)
        self.assertIn("Troubleshoot does not", architecture_plain)
        self.assertIn("system, storage, and security probes execute concurrently", architecture)
        self.assertIn("269 command definitions", c4)
        self.assertIn("Troubleshoot cannot mint an approval receipt", c4)
        self.assertNotIn("263 native command definitions", c4)
        self.assertIn("Home's hero/evidence composition stacks below 820 DIP", design)
        self.assertIn("Home's hero/evidence composition stacks below 820 DIP", ux_contract)
        self.assertNotIn("Home channels stack below 600 DIP", ux_contract)
        self.assertIn("installed extensions", ux_contract)
        self.assertIn("never infers Undo", ux_contract)


if __name__ == "__main__":
    unittest.main()
