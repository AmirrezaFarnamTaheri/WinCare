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
            '"Apply destructive change"',
            "SetSuccessfulPreview(previewSuccess)",
            "ResetReviewState();",
        ):
            self.assertIn(token, view_model)

        # ADR-001 narrows the two-phase gate to irreversible work only. Moderate
        # maintenance runs on one click, so the approval switch must not be offered
        # for it -- but a destructive command must never lose the gate.
        self.assertIn("RequiresApprovalSwitch => IsDestructiveTool", view_model)

        xaml = self.read("src/WinCare.App/Views/Pages/AllToolsPage.xaml")
        self.assertIn(
            'IsEnabled="{x:Bind ViewModel.Execution.CanApproveReview, Mode=OneWay}"',
            xaml,
        )

    def test_hardlink_consolidation_is_explicitly_disabled_at_catalog_and_executor(self) -> None:
        # Static defense-in-depth gate; behavioral admission is tested by the C# suites.
        import json
        catalog = json.loads(self.read("src/WinCare.CommandCatalog/Data/commands.json"))["commands"]
        hardlink = next(item for item in catalog if item["id"] == "storage-dedup-hardlink")
        self.assertEqual("ContractVerified", hardlink["migrationStatus"])
        self.assertEqual("High", hardlink["risk"])
        self.assertFalse(hardlink["readOnly"])
        native = self.read("src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.RemainingPillars.cs")
        self.assertIn('"storage.dedup.hardlink_unavailable"', native)
        self.assertNotIn("CreateHardLinkW", native)
        executor = self.read("src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.cs")
        self.assertIn('definition.Id.Equals("storage-dedup-hardlink"', executor)
        scanner = self.read("src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.StorageDeduplication.cs")
        self.assertIn("AttributesToSkip = FileAttributes.ReparsePoint", scanner)

    def test_unsafe_or_incomplete_commands_are_suspended_consistently(self) -> None:
        # Static check supplements (but does not replace) the Windows runtime tests.
        import json
        import re
        catalog = {command["id"]: command for command in
                   json.loads(self.read("src/WinCare.CommandCatalog/Data/commands.json"))["commands"]}
        executor = self.read("src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.cs")
        suspended = set(re.findall(r'^        "([a-z0-9-]+)",$',
                      executor.split("SuspendedMutationCommands", 1)[1].split("};", 1)[0],
                      flags=re.MULTILINE))
        self.assertGreaterEqual(len(suspended), 14)
        self.assertEqual(suspended, {
            identifier for identifier, command in catalog.items()
            if command["migrationStatus"] == "ContractVerified" and not command["readOnly"]
        })
        for identifier in suspended:
            with self.subTest(command=identifier):
                self.assertEqual("ContractVerified", catalog[identifier]["migrationStatus"])
                self.assertNotEqual("Low", catalog[identifier]["risk"])
                self.assertIn("Unavailable:", catalog[identifier]["summary"])
                self.assertIn("unavailable", catalog[identifier]["title"].lower())
        self.assertIn('if (SuspendedMutationCommands.Contains(definition.Id))', executor)
        # Console daemon lacks an SCM ServiceMain. Registry settings alone do not
        # register a toast producer, so neither can be advertised as installed.
        guard = self.read("src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.RemainingPillars.cs")
        rust = self.read("native/wincare-guard/src/main.rs")
        self.assertIn('"guard.scm_unavailable"', guard)
        self.assertIn('"guard.toast_unavailable"', guard)
        self.assertNotIn('"sc.exe", ["create"', guard)
        self.assertIn('fn main()', rust)
        self.assertNotIn('CommandHandlerOutcome.Success("Installer caches successfully purged.")', executor)
        self.assertNotIn('CommandHandlerOutcome.Success("Application residuals successfully purged.")', executor)
        self.assertNotIn('CommandHandlerOutcome.Success("Startup applications optimized successfully.")', executor)

    def test_high_risk_servicing_and_database_commands_require_review(self) -> None:
        import json
        catalog = {command["id"]: command for command in
                   json.loads(self.read("src/WinCare.CommandCatalog/Data/commands.json"))["commands"]}
        self.assertEqual("High", catalog["cleaner-dism-component-store"]["risk"])
        for command_id in ("compress-compactos", "sqlite-vacuum"):
            self.assertEqual("Moderate", catalog[command_id]["risk"])
        executor = self.read("src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.cs")
        self.assertIn('"cleaner-dism-component-store" => new[]', executor)
        self.assertIn('/ResetBase (prevents uninstalling existing updates)', executor)

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
