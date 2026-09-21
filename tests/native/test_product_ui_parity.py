from __future__ import annotations

import html
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class ProductUiParityTests(unittest.TestCase):
    def read(self, relative: str) -> str:
        return (ROOT / relative).read_text(encoding="utf-8")

    def test_home_guides_work_without_owning_system_commands(self) -> None:
        view_model = self.read("src/WinCare.App/ViewModels/Pages/HomePageViewModel.cs")
        xaml = self.read("src/WinCare.App/Views/Pages/HomePage.xaml")
        code_behind = self.read("src/WinCare.App/Views/Pages/HomePage.xaml.cs")

        for stale_owner in (
            "CommandDispatcher",
            "INativeSystemProbeRepository",
            "QuickCleanCommand",
            "StartupBoostCommand",
            "NetworkRefreshCommand",
        ):
            self.assertNotIn(stale_owner, view_model)
        self.assertIn("Home never runs system commands", view_model)
        self.assertEqual(1, xaml.count('Content="Open checkup"'))
        self.assertIn('Text="Common care"', xaml)
        self.assertIn("PageNavigation.NavigateToSection", code_behind)
        self.assertIn("CheckupCoverageText", view_model)
        self.assertIn("CheckupTimestamp", view_model)
        self.assertNotIn("EvidenceScoreText", view_model)
        self.assertNotIn("EvidenceSummaryCard", xaml)
        self.assertIn("record.CompletedAt ?? record.StartedAt", view_model)

    def test_checkup_is_read_only_and_uses_named_destinations(self) -> None:
        view_model = self.read("src/WinCare.App/ViewModels/Pages/CheckupPageViewModel.cs")
        page = self.read("src/WinCare.App/Views/Pages/CheckupPage.xaml.cs")
        row = self.read("src/WinCare.App/ViewModels/Pages/PageRow.cs")
        navigation = self.read("src/WinCare.App/Views/PageNavigation.cs")
        xaml = self.read("src/WinCare.App/Views/Pages/CheckupPage.xaml")

        self.assertIn('CommandRequest.Preview(WuaCommandId)', view_model)
        self.assertIn('SystemCareRoute = NavigationCatalog.Items.Single(item => item.Id == "system-care").Id', view_model)
        self.assertIn('SetNavigationAction(row, "Review updates", SystemCareRoute, "Network & updates")', view_model)
        self.assertIn("NavigationSectionTitle", view_model)
        self.assertIn("PageNavigation.NavigateToSection", page)
        self.assertNotIn("NavigationSectionIndex", row)
        self.assertNotIn("NavigationSectionIndex", view_model)
        self.assertNotIn("parameter is int", navigation)
        self.assertIn('Text="Result"', xaml)
        self.assertNotIn('Text="Evidence"', xaml)
        self.assertIn('AutomationProperties.Name="{x:Bind ActionAccessibleName, Mode=OneWay}"', xaml)

    def test_troubleshoot_suggests_but_never_owns_execution_or_undo(self) -> None:
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
        self.assertIn("Power tools", xaml)
        self.assertNotIn("Suggestions only", xaml)
        self.assertIn("bool IsReadOnly", action_plan)
        self.assertIn("AdministratorAccess AccessRequirement", action_plan)
        self.assertNotIn("UndoAvailable", action_plan)
        self.assertNotIn("RequiresElevation", action_plan)
        self.assertIn("IsReadOnly: match.ReadOnly", translator)
        self.assertIn("AccessRequirement: match.AdministratorAccess", translator)

    def test_care_rows_open_the_power_tools_inspector(self) -> None:
        control = self.read("src/WinCare.App/Controls/CareToolList.xaml.cs")
        xaml = self.read("src/WinCare.App/Controls/CareToolList.xaml")

        self.assertIn("PageNavigation.OpenTool", control)
        self.assertNotIn("PageNavigation.OpenTools", control)
        self.assertIn("CommandParameters is JsonElement", control)
        self.assertIn("Power tools", xaml)

    def test_power_tools_uses_named_controls_and_product_safety_tiers(self) -> None:
        xaml = self.read("src/WinCare.App/Views/Pages/AllToolsPage.xaml")
        code_behind = self.read("src/WinCare.App/Views/Pages/AllToolsPage.xaml.cs")
        view_model = self.read("src/WinCare.App/ViewModels/Pages/AllToolsPageViewModel.cs")
        row = self.read("src/WinCare.App/ViewModels/Pages/ToolRowViewModel.cs")

        for name in ("AreaFilter", "SectionFilter", "RiskFilter", "ReadOnlyFilter", "ResultCountText", "ParameterExpander"):
            self.assertIn(f'x:Name="{name}"', xaml)
        self.assertIn('AutomationProperties.AutomationId="ExecuteSelectedTool"', xaml)
        self.assertIn('AutomationProperties.AutomationId="CommandResultDetails"', xaml)
        self.assertNotIn("FindVisualDescendant", code_behind)
        self.assertNotIn("OfType<ComboBox>", code_behind)
        for label, tier in (("Safe", "Safe"), ("Moderate", "Moderate"), ("Destructive", "Destructive")):
            self.assertIn(f'new RiskFilterOption("{label}", RiskTier.{tier})', view_model)
        self.assertIn("Definition.RiskTier", row)

    def test_extensions_keep_trust_state_in_details_without_console_styling(self) -> None:
        view_model = self.read("src/WinCare.App/ViewModels/Pages/PluginStorePageViewModel.cs")
        card = self.read("src/WinCare.App/ViewModels/Pages/PluginCardViewModel.cs")
        xaml = self.read("src/WinCare.App/Views/Pages/PluginStorePage.xaml")
        dialog = self.read("src/WinCare.App/Views/Dialogs/PluginDetailDialog.xaml")

        self.assertIn("CatalogStatusMessage", view_model)
        self.assertIn("IsCatalogTrustVerified", view_model)
        self.assertIn('Header="Online catalog"', xaml)
        self.assertIn('AutomationProperties.AutomationId="PluginCatalogStatus"', xaml)
        self.assertIn("catalog and package checks pass", dialog)
        self.assertIn('Content="Details"', xaml)
        for property_name in (
            "DetailsAccessibleName",
            "InstallAccessibleName",
            "EnableAccessibleName",
            "DisableAccessibleName",
            "UninstallAccessibleName",
        ):
            self.assertIn(property_name, card)
            self.assertIn(f'AutomationProperties.Name="{{x:Bind {property_name}}}"', xaml)
        self.assertNotIn('Text="{x:Bind StatusBadgeText}" FontFamily="{StaticResource TelemetryFontFamily}"', xaml)

    def test_first_run_tour_is_progressive_persisted_and_repeatable(self) -> None:
        preferences = self.read("src/WinCare.App/Services/AppPreferences.cs")
        shell = self.read("src/WinCare.App/Views/ShellPage.xaml.cs")
        help_xaml = self.read("src/WinCare.App/Views/Pages/HelpPage.xaml")
        help_code = self.read("src/WinCare.App/Views/Pages/HelpPage.xaml.cs")
        tour = self.read("src/WinCare.App/Views/Dialogs/FirstRunTourDialog.xaml")
        tour_code = self.read("src/WinCare.App/Views/Dialogs/FirstRunTourDialog.xaml.cs")

        self.assertIn("HasSeenFirstRunTour", preferences)
        self.assertIn("MarkFirstRunTourSeen", preferences)
        self.assertIn("!AppPreferences.HasSeenFirstRunTour", shell)
        self.assertIn("new FirstRunTourDialog", shell)
        self.assertIn('Content="Take the tour"', help_xaml)
        self.assertIn("PageNavigation.ShowTourAsync", help_code)
        for step in ("CheckupStep", "CareStep", "FindStep"):
            self.assertIn(f'x:Name="{step}"', tour)
        self.assertIn('PrimaryButtonText="Next"', tour)
        self.assertIn('SecondaryButtonText="Skip tour"', tour)
        self.assertIn("args.Cancel = true", tour_code)
        self.assertIn('PrimaryButtonText = _step == LastStep ? "Done" : "Next"', tour_code)
        self.assertNotIn("TourService", shell)

    def test_pull_request_template_is_general(self) -> None:
        template = self.read(".github/pull_request_template.md")

        for repo_specific in ("Command dispatcher", "Plugins / catalog trust", "Guard / IPC", "Rust native core", "supply-chain"):
            self.assertNotIn(repo_specific, template)
        for section in ("## Summary", "## Changes", "## Risk", "## Validation", "## Follow-up"):
            self.assertIn(section, template)

    def test_global_search_does_not_hide_registry_errors(self) -> None:
        main_window = self.read("src/WinCare.App/MainWindow.xaml.cs")
        search_service = self.read("src/WinCare.Application/Navigation/GlobalSearchService.cs")

        # Ranking moved into GlobalSearchService (wave 4); the contract is unchanged:
        # the shell wires the real registry in, the service enumerates it directly,
        # and no empty catch may swallow failures anywhere on that path.
        self.assertIn("new(AppRuntime.Current.ToolCatalog, AppRuntime.Current.PluginRegistry)", main_window)
        self.assertIn("_extensions.GetAllPlugins()", search_service)
        self.assertNotIn("catch { }", main_window)
        self.assertNotIn("catch { }", search_service)
        self.assertNotIn("How reviews and approvals work", main_window)

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
        self.assertEqual(1, shell_code.count("_pendingParameter" + " = parameter"))
        self.assertNotIn("_pendingToolsParameter", shell_code)

    def test_chrome_labels_agree_across_catalog_xaml_and_resources(self) -> None:
        # Wave 4 i18n audit: en-US is the only shipped locale, but nav/page chrome labels
        # exist in three places (NavigationCatalog, XAML Content/Text, Resources.resw).
        # Pin them together so a rename cannot silently drift one copy.
        catalog = self.read("src/WinCare.Application/Navigation/NavigationCatalog.cs")
        resw = self.read("src/WinCare.App/Strings/en-US/Resources.resw")

        resw_values = {
            (uid, prop): value
            for uid, prop, value in re.findall(
                r'<data name="([^.]+)\.(\w+)"[^>]*><value>(.*?)</value></data>', resw)
        }

        xaml_files = [ROOT / "src/WinCare.App/Views/ShellPage.xaml",
                      *sorted((ROOT / "src/WinCare.App/Views/Pages").glob("*.xaml"))]
        uid_count = 0
        for path in xaml_files:
            source = path.read_text(encoding="utf-8")
            for attrs in re.findall(r'<\w+(?:\.\w+)*\s((?:[^>"]|"[^"]*")*?/?)>', source):
                uid_match = re.search(r'x:Uid="([^"]+)"', attrs)
                if uid_match is None:
                    continue
                uid = uid_match.group(1)
                for prop in ("Content", "Text"):
                    prop_match = re.search(rf'{prop}="([^"]*)"', attrs)
                    if prop_match is None:
                        continue
                    uid_count += 1
                    self.assertIn((uid, prop), resw_values, f"{path.name}: {uid}.{prop} missing from Resources.resw")
                    self.assertEqual(prop_match.group(1), resw_values[(uid, prop)],
                                     f"{path.name}: {uid}.{prop} differs from Resources.resw")
        self.assertGreater(uid_count, 15)

        catalog_labels = set(re.findall(r'new\("[^"]+", "([^"]+)"', catalog))
        nav_labels = {html.unescape(value) for (uid, prop), value in resw_values.items()
                      if uid.startswith("Nav") and prop == "Content"}
        self.assertEqual(catalog_labels, nav_labels)

    def test_activity_copy_is_plain_language(self) -> None:
        activity = self.read("src/WinCare.App/Views/Pages/ActivityPage.xaml")
        view_model = self.read("src/WinCare.App/ViewModels/Pages/ActivityPageViewModel.cs")

        for jargon in ("pending confirmations", "elevated confirmation", "not durable", "Review needed", "second look"):
            self.assertNotIn(jargon, activity)
        self.assertIn("what finished", activity)
        self.assertIn("Recent work shows up here", activity)
        self.assertNotIn('FontFamily="{StaticResource TelemetryFontFamily}"', activity)
        self.assertIn('new PageSection("History"', view_model)

    def test_legacy_instrument_panel_styles_are_removed(self) -> None:
        controls = self.read("src/WinCare.App/Styles/ControlStyles.xaml")
        theme = self.read("src/WinCare.App/Styles/ThemeResources.xaml")
        for legacy in ("DoubleBezel", "HudChassis", "LuminousGlow", "IslandIcon", "TelemetrySensorBox", "EyebrowBadge"):
            self.assertNotIn(legacy, controls)
            self.assertNotIn(legacy, theme)

    def test_product_docs_still_match_execution_and_responsive_contracts(self) -> None:
        guide = self.read("docs/User-Guide.md")
        architecture = self.read("docs/Architecture.md").replace("**", "")
        design = self.read("DESIGN.md")
        ux_contract = self.read("UX-CONTRACT.md")

        self.assertIn("fast system/storage/security probes run concurrently", guide)
        self.assertIn("Power tools applies the normal risk-tier flow", guide)
        self.assertIn("Power tools is the canonical advanced command inspector and execution surface", architecture)
        self.assertIn("Home's hero/evidence composition stacks below 820 DIP", design)
        self.assertIn("Home's hero/evidence composition stacks below 820 DIP", ux_contract)
        self.assertNotIn("Home channels stack below 600 DIP", ux_contract)


if __name__ == "__main__":
    unittest.main()
