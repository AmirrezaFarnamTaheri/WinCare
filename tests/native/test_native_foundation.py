from __future__ import annotations

import unittest

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
            "ActivityPage.xaml", "SettingsPage.xaml",
        )
        for name in page_names:
            text = (root / "src/WinCare.App/Views/Pages" / name).read_text(encoding="utf-8")
            self.assertIn("ListView", text, name)
            self.assertIn("What it does", text, name)
            self.assertIn("State", text, name)
            self.assertIn("Notes", text, name)
            self.assertIn("IsCompact", text, name)
            self.assertIn("SizeChanged", text, name)

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
