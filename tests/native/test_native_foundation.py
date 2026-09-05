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
            "SecurityPage.xaml", "RepairRecoveryPage.xaml",
            "ActivityPage.xaml",
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

        checkup_code = (root / "src/WinCare.App/Views/Pages/CheckupPage.xaml.cs").read_text(encoding="utf-8")
        self.assertIn("LayoutVisibility.IsCompact(e.NewSize.Width)", checkup_code)
        self.assertNotIn("CompactThreshold", checkup_code)

        checkup_vm = (root / "src/WinCare.App/ViewModels/Pages/CheckupPageViewModel.cs").read_text(encoding="utf-8")
        self.assertNotIn("Parallel.ForEachAsync", checkup_vm)
        self.assertIn("foreach ((string commandId, string rowTitle) in QuickCheckCommands)", checkup_vm)

        settings = (root / "src/WinCare.App/Views/Pages/SettingsPage.xaml").read_text(encoding="utf-8")
        self.assertIn("ThemeSelector", settings)
        self.assertIn("OpenDataFolderButton_Click", settings)

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
            "ActivityPage.xaml",
            "CheckupPage.xaml",
            "SystemCarePage.xaml",
            "SecurityPage.xaml",
            "RepairRecoveryPage.xaml",
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

    def test_package_profiles_are_clean_and_stage_native_core(self) -> None:
        root = __import__("pathlib").Path(__file__).resolve().parents[2]
        profiles = sorted((root / "src/WinCare.App/Properties/PublishProfiles").glob("*.pubxml"))
        self.assertEqual(4, len(profiles))
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


if __name__ == "__main__":
    unittest.main()
