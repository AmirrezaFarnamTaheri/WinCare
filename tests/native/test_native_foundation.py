from __future__ import annotations

import unittest
import xml.etree.ElementTree as ET

from tools.verify_native_foundation import load_oracle_commands, verify


class NativeFoundationTests(unittest.TestCase):
    def test_frozen_oracle_contains_exactly_259_unique_commands(self) -> None:
        commands = load_oracle_commands()
        self.assertEqual(259, len(commands))
        self.assertEqual(259, len(set(commands)))

    def test_native_foundation_passes_structural_gate(self) -> None:
        findings = verify()
        self.assertEqual([], findings, "\n" + "\n".join(f"[{item.code}] {item.message}" for item in findings))

    def test_native_pages_use_supported_backdrop_and_responsive_tables(self) -> None:
        root = __import__("pathlib").Path(__file__).resolve().parents[2]
        window = (root / "src/WinCare.App/MainWindow.xaml.cs").read_text(encoding="utf-8")
        self.assertIn("MicaController.IsSupported", window)
        self.assertIn("DesktopAcrylicBackdrop", window)
        page_names = (
            "HomePage.xaml", "CheckupPage.xaml", "SystemCarePage.xaml",
            "SecurityPage.xaml", "RepairRecoveryPage.xaml", "ActivityPage.xaml",
        )
        for name in page_names:
            text = (root / "src/WinCare.App/Views/Pages" / name).read_text(encoding="utf-8")
            if name in {"HomePage.xaml", "CheckupPage.xaml"}:
                self.assertIn("DashboardCardStyle", text, name)
                self.assertIn("Review before applying", text, name)
                self.assertIn("SizeChanged", text, name)
                if name == "CheckupPage.xaml":
                    self.assertIn("SelectorBar", text, name)
                    self.assertIn("LayoutVisibility.BoolToVisibility(ViewModel.IsCompactLayout)", text, name)
                    self.assertIn("LayoutVisibility.InvertBoolToVisibility(ViewModel.IsCompactLayout)", text, name)
                    self.assertIn("LayoutVisibility.BoolToVisibility(IsCompact)", text, name)
                continue

            expected_headers = (
                ("Summary", "State", "Time")
                if name == "ActivityPage.xaml"
                else ("What it does", "State", "Notes")
            )
            self.assertIn("ListView", text, name)
            for header in expected_headers:
                self.assertIn(header, text, name)
            self.assertIn("IsCompact", text, name)
            self.assertIn("SizeChanged", text, name)

        shared_breakpoint_pages = (
            "CheckupPage.xaml.cs", "SystemCarePage.xaml.cs", "SecurityPage.xaml.cs", "RepairRecoveryPage.xaml.cs",
        )
        for name in shared_breakpoint_pages:
            code = (root / "src/WinCare.App/Views/Pages" / name).read_text(encoding="utf-8")
            self.assertIn("LayoutVisibility.IsCompact(e.NewSize.Width)", code, name)
            self.assertNotIn("< 820", code, name)
            self.assertNotIn("CompactThreshold", code, name)

        checkup_vm = (root / "src/WinCare.App/ViewModels/Pages/CheckupPageViewModel.cs").read_text(encoding="utf-8")
        sequential_probe_runner = (root / "src/WinCare.Application/Commands/SequentialCommandProbeRunner.cs").read_text(encoding="utf-8")
        parallel_probe_runner = (root / "src/WinCare.Application/Commands/ParallelCommandProbeRunner.cs").read_text(encoding="utf-8")
        self.assertNotIn("Parallel.ForEachAsync", checkup_vm)
        self.assertIn("ParallelCommandProbeRunner.RunPreviewsAsync", checkup_vm)
        self.assertIn("foreach (string commandId in commandIds)", sequential_probe_runner)
        self.assertNotIn("Task.WhenAll", sequential_probe_runner)
        self.assertIn("public static class ParallelCommandProbeRunner", parallel_probe_runner)
        self.assertIn("RunPreviewsAsync(", parallel_probe_runner)
        self.assertIn("SemaphoreSlim", parallel_probe_runner)
        self.assertIn("Task.WhenAll", parallel_probe_runner)
        self.assertIn("tasks[i] = RunSingleProbeAsync", parallel_probe_runner)

        plugin_store = (root / "src/WinCare.App/Views/Pages/PluginStorePage.xaml").read_text(encoding="utf-8")
        self.assertIn('MinWindowWidth="920"', plugin_store)
        self.assertIn("PluginCatalogTrustStatus", plugin_store)
        self.assertIn("CatalogStatusMessage", plugin_store)

        settings = (root / "src/WinCare.App/Views/Pages/SettingsPage.xaml").read_text(encoding="utf-8")
        self.assertIn("ThemeSelector", settings)
        self.assertIn("OpenDataFolderButton_Click", settings)
        self.assertIn("RememberWindowPlacement", settings)

    def test_plugin_catalog_is_browse_only_without_an_explicit_pinned_root(self) -> None:
        root = __import__("pathlib").Path(__file__).resolve().parents[2]
        runtime = (root / "src/WinCare.App/Services/AppRuntime.cs").read_text(encoding="utf-8")
        catalog = (root / "src/WinCare.Infrastructure/Plugins/RemoteCatalogService.cs").read_text(encoding="utf-8")
        view_model = (root / "src/WinCare.App/ViewModels/Pages/PluginStorePageViewModel.cs").read_text(encoding="utf-8")
        self.assertIn("CatalogService = new RemoteCatalogService();", runtime)
        self.assertIn("remote installation is disabled because this build has no pinned catalog signing key", catalog)
        self.assertIn("if (!catalog.IsTrustVerified)", view_model)
        self.assertIn("CatalogStatusMessage = catalog.TrustStatusMessage", view_model)

    def test_window_continuity_is_a_real_opt_out_preference(self) -> None:
        root = __import__("pathlib").Path(__file__).resolve().parents[2]
        preferences = (root / "src/WinCare.App/Services/AppPreferences.cs").read_text(encoding="utf-8")
        window = (root / "src/WinCare.App/MainWindow.xaml.cs").read_text(encoding="utf-8")
        settings_vm = (root / "src/WinCare.App/ViewModels/Pages/SettingsPageViewModel.cs").read_text(encoding="utf-8")
        self.assertIn("RememberWindowPlacement", preferences)
        self.assertIn("WindowPlacement = value ? _current.WindowPlacement : null", preferences)
        self.assertIn("!AppPreferences.RememberWindowPlacement || !RestoreWindowPlacement()", window)
        self.assertIn("if (AppPreferences.RememberWindowPlacement)", window)
        self.assertIn("AppPreferences.RememberWindowPlacement = value", settings_vm)

    def test_interactive_controls_are_wired_and_mutable_rows_use_live_bindings(self) -> None:
        root = __import__("pathlib").Path(__file__).resolve().parents[2]
        pages = root / "src/WinCare.App/Views/Pages"
        inert_controls: list[str] = []

        for path in pages.glob("*.xaml"):
            document = ET.fromstring(path.read_text(encoding="utf-8"))
            for element in document.iter():
                control = element.tag.rsplit("}", 1)[-1]
                if control in {"Button", "ToggleButton", "HyperlinkButton", "MenuFlyoutItem"}:
                    if not any(name.rsplit("}", 1)[-1] in {"Click", "Command", "NavigateUri"} for name in element.attrib):
                        inert_controls.append(f"{path.name}: {control} {element.attrib.get('Content', '')}")

        self.assertEqual([], inert_controls)

        mutable_row_pages = (
            "ActivityPage.xaml", "CheckupPage.xaml", "SystemCarePage.xaml",
            "SecurityPage.xaml", "RepairRecoveryPage.xaml",
        )
        for name in mutable_row_pages:
            text = (pages / name).read_text(encoding="utf-8")
            self.assertNotIn('Text="{x:Bind State}"', text, name)
            self.assertNotIn('Text="{x:Bind Detail}"', text, name)

        doctor = (pages / "AiDoctorPage.xaml.cs").read_text(encoding="utf-8")
        self.assertLess(doctor.index("ViewModel = new AiDoctorPageViewModel()"), doctor.index("InitializeComponent()"))

        checkup = (pages / "CheckupPage.xaml").read_text(encoding="utf-8")
        self.assertNotIn('Text="Full check"', checkup)
        self.assertNotIn('Text="Custom check"', checkup)

    def test_design_and_runtime_evidence_docs_do_not_overclaim_current_state(self) -> None:
        root = __import__("pathlib").Path(__file__).resolve().parents[2]
        design = (root / "DESIGN.md").read_text(encoding="utf-8")
        screenshots = (root / "docs/Screenshots.md").read_text(encoding="utf-8")
        readme = (root / "README.md").read_text(encoding="utf-8")
        self.assertIn("No generic Undo claim", design)
        self.assertIn("LayoutVisibility.CompactBreakpointDip = 920.0", design)
        self.assertIn("historical runtime evidence", screenshots)
        self.assertIn("needs recapture", screenshots)
        self.assertIn("remote plugin installation intentionally", readme)
        self.assertIn("wincare-guard` is **experimental**", readme)

    def test_package_profiles_are_clean_and_stage_native_core(self) -> None:
        root = __import__("pathlib").Path(__file__).resolve().parents[2]
        profiles = sorted((root / "src/WinCare.App/Properties/PublishProfiles").glob("*.pubxml"))
        self.assertEqual(6, len(profiles), sorted(profile.name for profile in profiles))
        for profile in profiles:
            text = profile.read_text(encoding="utf-8")
            invalid = [character for character in text if ord(character) < 32 and character not in "\n\r\t"]
            self.assertEqual([], invalid, profile.name)
            if profile.name.startswith("portable-"):
                self.assertIn("artifacts", text, profile.name)

        project = (root / "src/WinCare.App/WinCare.App.csproj").read_text(encoding="utf-8")
        self.assertIn("Native\\$(Platform)\\wincare_core.dll", project)
        self.assertIn("CopyToPublishDirectory", project)

    def test_startup_path_is_instrumented_and_probe_free(self) -> None:
        root = __import__("pathlib").Path(__file__).resolve().parents[2]
        app = (root / "src/WinCare.App/App.xaml.cs").read_text(encoding="utf-8")
        window = (root / "src/WinCare.App/MainWindow.xaml.cs").read_text(encoding="utf-8")
        shell = (root / "src/WinCare.App/Views/ShellPage.xaml.cs").read_text(encoding="utf-8")
        telemetry = root / "src/WinCare.Infrastructure/Observability/StartupTelemetry.cs"
        self.assertTrue(telemetry.is_file())
        self.assertIn("public void StartupStage", telemetry.read_text(encoding="utf-8"))
        combined = "\n".join((app, window, shell, telemetry.read_text(encoding="utf-8")))
        for marker in ("AppConstructed", "WindowCreated", "FirstContentRendered", "ShellInteractive"):
            self.assertIn(marker, combined)
        for banned in ("HttpClient", "Process.Start", "System.Management", "ManagementObject", "Dns."):
            self.assertNotIn(banned, app + window + shell)

    def test_windows_ci_runs_managed_tests_only_on_x64_and_pins_python(self) -> None:
        root = __import__("pathlib").Path(__file__).resolve().parents[2]
        workflow = (root / ".github/workflows/native-winui.yml").read_text(encoding="utf-8")
        self.assertIn('python-version: "3.13"', workflow)
        managed_test_marker = "      - name: Managed tests\n        if: matrix.platform == 'x64'"
        self.assertIn(managed_test_marker, workflow)
        self.assertIn("python -m unittest discover -s tests/native -v", workflow)

    def test_windows_ci_validates_real_trimmed_portable_artifacts(self) -> None:
        root = __import__("pathlib").Path(__file__).resolve().parents[2]
        workflow = (root / ".github/workflows/native-winui.yml").read_text(encoding="utf-8")
        app = (root / "src/WinCare.App/App.xaml.cs").read_text(encoding="utf-8")
        checklist = (root / "tools/release_checklist.py").read_text(encoding="utf-8")

        self.assertIn("PORTABLE_EXECUTABLE_MAX_BYTES = 70_000_000", checklist)
        self.assertIn("--portable-artifact", checklist)
        self.assertIn("Validate portable executable size", workflow)
        self.assertIn("python tools/release_checklist.py --portable-artifact", workflow)
        self.assertIn("portable-runtime-smoke:", workflow)
        self.assertIn("windows-11-vs2026-arm", workflow)
        self.assertIn("--smoke-test", workflow)
        self.assertIn("portable-runtime-smoke", workflow.split("release-gate:", 1)[1])
        self.assertIn('PortableSmokeArgument = "--smoke-test"', app)
        self.assertIn('CommandRequest.Preview("system")', app)
        self.assertIn("GetAbiVersion()", app)


if __name__ == "__main__":
    unittest.main()
