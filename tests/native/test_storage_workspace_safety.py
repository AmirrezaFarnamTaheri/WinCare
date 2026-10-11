"""Fail-closed source gates for the WinUI Files & storage workspace.

These checks cannot replace interactive Explorer, file-system, and cancellation
tests on Windows; they prevent known dangerous patterns from reappearing.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]


class StorageWorkspaceSafetyTests(unittest.TestCase):
    @staticmethod
    def source() -> str:
        return (ROOT / "src/WinCare.App/Views/Pages/StorageBrowserPage.xaml.cs").read_text(
            encoding="utf-8"
        )

    def test_cancelled_measurements_cannot_crash_async_ui_handler(self) -> None:
        source = self.source()
        self.assertIn('catch (OperationCanceledException) when (cancellation.IsCancellationRequested)', source)
        self.assertIn('_reportCancellation?.Cancel();', source)
        self.assertIn('if (!_visible || cancellation.IsCancellationRequested || _currentPath != root)', source)
        self.assertIn('private void UpdateScanProgress()', source)
        self.assertIn('_browseBusy || _scanBusy', source)

    def test_back_history_is_committed_only_after_successful_navigation(self) -> None:
        source = self.source()
        self.assertIn('private async Task<bool> NavigateAsync', source)
        self.assertIn('if (await NavigateAsync(previous, remember: false)', source)
        self.assertNotIn('NavigateAsync(_back.Pop()', source)

    def test_open_location_never_shell_executes_selected_file(self) -> None:
        source = self.source()
        self.assertIn('Process.Start(new ProcessStartInfo(explorer)', source)
        self.assertIn('UseShellExecute = false', source)
        self.assertIn('ArgumentList = { folder }', source)
        self.assertNotIn('new ProcessStartInfo(path) { UseShellExecute = true }', source)


if __name__ == "__main__":
    unittest.main()
