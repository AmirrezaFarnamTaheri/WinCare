"""Only real, same-run checkup outcomes may populate Home checkup status."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]

class CheckupSessionWiringTests(unittest.TestCase):
    def test_home_ignores_unrelated_activity_for_checkup_coverage(self):
        source = (ROOT / "src/WinCare.App/ViewModels/Pages/HomePageViewModel.cs").read_text()
        self.assertIn("CheckupSessionSnapshot? checkup", source)
        self.assertNotIn("latestByCommand", source)
        self.assertIn("checkup.Areas.Count", source)

    def test_checkup_writes_a_single_session_including_terminal_cancellation(self):
        source = (ROOT / "src/WinCare.App/ViewModels/Pages/CheckupPageViewModel.cs").read_text()
        self.assertIn('SaveSessionAsync(sessionId, startedAt, "Running"', source)
        self.assertIn('SaveSessionAsync(sessionId, startedAt, "Completed"', source)
        self.assertIn('cancelled ? "Cancelled" : "Incomplete"', source)
        self.assertIn("CheckupSessionStore.ProbeIds", source)
        self.assertIn("MapAreaFindingsIntoSession(probeStates)", source)
        self.assertNotIn('ConfigureAwait(true);\n        {\n            foreach', source)

    def test_app_shared_store_drives_home_and_capture_mode_isolated_data_root(self):
        page = (ROOT / "src/WinCare.App/Views/Pages/HomePage.xaml.cs").read_text()
        runtime = (ROOT / "src/WinCare.App/Services/AppRuntime.cs").read_text()
        self.assertIn("AppRuntime.Current.Checkups.ReadLatest()", page)
        self.assertIn("AppRuntime.Current.Checkups.Changed", page)
        self.assertIn("new CheckupSessionStore()", runtime)

if __name__ == "__main__":
    unittest.main()
