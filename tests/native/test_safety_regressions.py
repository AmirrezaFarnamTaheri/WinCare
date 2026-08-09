from __future__ import annotations

from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]


class SafetyRegressionTests(unittest.TestCase):
    def read(self, relative: str) -> str:
        return (ROOT / relative).read_text(encoding="utf-8")

    def test_disk_cleanup_enforces_boundary_and_reparse_safe_walk(self) -> None:
        source = self.read("src/WinCare.Application/Commands/Handlers/DiskCleanupCommandHandler.cs")
        self.assertIn("normalized.Equals(allowed, StringComparison.OrdinalIgnoreCase)", source)
        self.assertIn("allowed + Path.DirectorySeparatorChar", source)
        self.assertIn("FileAttributes.ReparsePoint", source)
        self.assertIn("disk_cleanup.target_denied", source)
        self.assertIn("disk_cleanup.measurement_failed", source)
        self.assertNotIn("SearchOption.AllDirectories", source)

    def test_cleanup_targets_has_no_event_log_mutation_path(self) -> None:
        source = self.read("src/WinCare.Application/Commands/Handlers/LogCleanupCommandHandler.cs")
        self.assertIn("log_cleanup.readonly", source)
        self.assertIn("log_cleanup.enumeration_failed", source)
        self.assertNotIn(".Clear()", source)
        self.assertNotIn("LogCleanupResultRecord", source)

    def test_privacy_handler_reports_registry_read_failures(self) -> None:
        source = self.read("src/WinCare.Application/Commands/Handlers/PrivacyStatusCommandHandler.cs")
        self.assertIn("privacy.read_failed", source)
        self.assertNotIn("Fallback default", source)

    def test_mutating_ui_requires_successful_preview_before_approval(self) -> None:
        source = self.read("src/WinCare.App/ViewModels/Pages/ToolExecutionViewModel.cs")
        for token in (
            "CanApproveReview",
            "value && CanApproveReview",
            '"Apply changes"',
            "SetSuccessfulPreview(result.Status == CommandResultStatus.Succeeded)",
            "ResetReviewState();",
        ):
            self.assertIn(token, source)

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
