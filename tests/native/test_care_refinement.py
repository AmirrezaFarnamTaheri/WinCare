"""Source-contract guards for care refinements; these do not replace live WinUI tests."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "src/WinCare.App"
XAML = {"x": "http://schemas.microsoft.com/winfx/2006/xaml"}


class CareRefinementTests(unittest.TestCase):
    def read(self, path):
        return (APP / path).read_text(encoding="utf-8-sig")

    def test_cancelled_checks_settle_before_releasing_the_run(self):
        source = self.read("ViewModels/Pages/CheckupPageViewModel.cs")
        self.assertNotIn("if (token.IsCancellationRequested) return;", source)
        self.assertEqual(2, source.count("token.ThrowIfCancellationRequested();"))
        self.assertIn("CompleteInterruptedCheck(cancelled: true);", source)
        final = source.split("finally", 1)[1].split("private async Task", 1)[0]
        self.assertLess(final.index("await updateTask;"), final.index("IsRunning = false;"))
        self.assertIn('HealthScoreText = cancelled ? "Stopped" : "Incomplete";', source)
        self.assertIn('row.State = "Not finished";', source)
        self.assertIn("RebuildResultRowsFromQuickChecks();", source.split("private void CompleteInterruptedCheck", 1)[1])

    def test_stop_action_has_pending_feedback_and_a_disabled_guard(self):
        source = self.read("ViewModels/Pages/CheckupPageViewModel.cs")
        page = self.read("Views/Pages/CheckupPage.xaml")
        self.assertIn("IsRunning && !IsStopping", source)
        self.assertIn('IsStopping ? "Stopping…"', source)
        self.assertIn("ViewModel.StopCheckCommand", page)
        self.assertIn('AutomationProperties.AutomationId="StopQuickCheck"', page)
        self.assertIn('AutomationProperties.LiveSetting="Polite"', page)

    def test_a_rerun_replaces_cached_results_before_awaiting_probes(self):
        source = self.read("ViewModels/Pages/CheckupPageViewModel.cs")
        run = source.split("private async Task RunQuickCheckAsync", 1)[1]
        self.assertLess(run.index("RebuildResultRowsFromQuickChecks();"), run.index("await ParallelCommandProbeRunner"))

    def test_compact_checkup_keeps_status_and_has_an_empty_results_message(self):
        source = self.read("Views/Pages/CheckupPage.xaml.cs")
        page = self.read("Views/Pages/CheckupPage.xaml")
        self.assertNotIn("CheckupStatusCard.Visibility = Visibility.Collapsed", source)
        self.assertIn("Grid.SetRow(CheckupStatusCard, compact ? 1 : 0)", source)
        self.assertIn("ViewModel.EmptyMessage", page)
        self.assertIn("ViewModel.IsEmpty", page)
        self.assertNotIn('Glyph="&#xE73E;"', page)  # no unconditional success checkmark

    def test_home_does_not_call_an_inactive_partial_check_running(self):
        source = self.read("ViewModels/Pages/HomePageViewModel.cs")
        self.assertIn("bool isChecking = latestByCommand.Values.Any", source)
        self.assertIn('CheckupTitle = "Checkup is incomplete";', source)
        self.assertIn('else if (isChecking)', source)

    def test_activity_points_to_a_real_section_and_shows_dates(self):
        page = self.read("Views/Pages/ActivityPage.xaml")
        source = self.read("ViewModels/Pages/ActivityPageViewModel.cs")
        self.assertNotIn("Open an item to see", page)
        self.assertIn('Click="ReviewAttention_Click"', page)
        self.assertIn('ToString("g")', source)
        self.assertIn('day.Key.ToString("dddd, MMM d, yyyy")', source)
        self.assertIn("entries.Max(record => record.CompletedAt ?? record.StartedAt)", source)
        self.assertIn('cancelled > 0 ? "Stopped"', source)

    def test_empty_care_list_does_not_show_table_chrome(self):
        page = self.read("Controls/CareToolList.xaml")
        tree = ET.fromstring(page)
        borders = [node for node in tree.iter() if node.tag.endswith("}Border")]
        self.assertTrue(any("ViewModel.IsEmpty" in node.get("Visibility", "") for node in borders))
        self.assertIn("Choosing a task opens its details in Power tools. It does not run it.", page)

    def test_owned_xaml_remains_well_formed(self):
        for path in ("Views/Pages/HomePage.xaml", "Views/Pages/CheckupPage.xaml",
                     "Views/Pages/ActivityPage.xaml", "Controls/CareToolList.xaml"):
            with self.subTest(path=path):
                tree = ET.fromstring(self.read(path))
                names = [n.attrib[f"{{{XAML['x']}}}Name"] for n in tree.iter()
                         if f"{{{XAML['x']}}}Name" in n.attrib]
                self.assertEqual(len(names), len(set(names)))


if __name__ == "__main__":
    unittest.main()
