from __future__ import annotations

from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]


class SafetyRegressionTests(unittest.TestCase):
    def read(self, relative: str) -> str:
        return (ROOT / relative).read_text(encoding="utf-8")

    def test_disk_cleanup_enforces_fixed_boundaries_and_reparse_safe_walk(self) -> None:
        source = self.read("src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Experience.cs")
        productivity = self.read("src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Productivity.cs")
        self.assertIn("Path.GetTempPath()", source)
        self.assertIn("SafeRecursiveEnumeration", source)
        self.assertIn("FileAttributes.ReparsePoint", source)
        self.assertNotIn("SearchOption.AllDirectories", source)
        self.assertIn("AttributesToSkip = FileAttributes.ReparsePoint", productivity)

    def test_cleanup_target_inventory_has_no_event_log_mutation_path(self) -> None:
        source = self.read("src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.System.cs")
        method = source[source.index("CleanupTargetsAsync"):source.index("private CommandHandlerOutcome StorageOverview")]
        self.assertIn("Cleanup targets measured without changing files", method)
        self.assertNotIn("EventLog", method)
        self.assertNotIn(".Clear(", method)
        self.assertNotIn("File.Delete", method)

    def test_platform_access_and_io_failures_do_not_fall_back_to_success(self) -> None:
        source = self.read("src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.cs")
        self.assertIn("catch (UnauthorizedAccessException ex)", source)
        self.assertIn('CommandHandlerOutcome.Blocked("command.access_denied"', source)
        self.assertIn("IOException", source)
        self.assertIn('"command.native_failure"', source)
        self.assertNotIn("Fallback default", source)

    def test_windows_update_result_codes_do_not_report_partial_failure_as_success(self) -> None:
        source = self.read("src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Security.cs")
        self.assertIn("WindowsUpdateOperationOutcome", source)
        self.assertIn("resultCode == 2", source)
        self.assertIn("CommandResultStatus.Failed", source)

    def test_mutating_ui_requires_successful_preview_before_approval(self) -> None:
        view_model = self.read("src/WinCare.App/ViewModels/Pages/ToolExecutionViewModel.cs")
        for token in (
            "CanApproveReview",
            "value && CanApproveReview",
            '"Apply changes"',
            "SetSuccessfulPreview(previewSuccess)",
            "ResetReviewState();",
        ):
            self.assertIn(token, view_model)

        xaml = self.read("src/WinCare.App/Views/Pages/AllToolsPage.xaml")
        self.assertIn(
            'IsEnabled="{x:Bind ViewModel.Execution.CanApproveReview, Mode=OneWay}"',
            xaml,
        )

    def test_native_directory_size_uses_iterative_walk(self) -> None:
        source = self.read("native/wincare-core/src/lib.rs")
        self.assertIn("let mut pending = vec![path.to_path_buf()]", source)
        self.assertIn("while let Some(current) = pending.pop()", source)
        self.assertIn("is_reparse_point(&metadata)", source)
        self.assertNotIn("let sub = accumulate_dir_size", source)
        self.assertIn("dir_size_handles_deep_directory_tree_without_recursion", source)

    def test_system_info_wrapper_bounds_probe_fill_retries(self) -> None:
        source = self.read("src/WinCare.Infrastructure/Native/NativeCoreService.cs")
        for token in (
            "SystemInfoMaxAttempts = 3",
            "ValidateSystemInfoLength(required)",
            "written > (nuint)buffer.Length",
            "written <= (nuint)buffer.Length",
            "output changed repeatedly during bounded retries",
        ):
            self.assertIn(token, source)


if __name__ == "__main__":
    unittest.main()
