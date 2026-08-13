from __future__ import annotations

import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CATALOG = ROOT / "src/WinCare.CommandCatalog/Data/commands.json"
HANDLERS = ROOT / "src/WinCare.Application/Commands/Handlers"


class FullNativeMigrationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.catalog = json.loads(CATALOG.read_text(encoding="utf-8"))["commands"]

    def test_all_259_commands_are_implemented_or_behavior_verified(self) -> None:
        self.assertEqual(259, len(self.catalog))
        self.assertEqual(259, len({item["id"] for item in self.catalog}))
        incomplete = {
            item["id"]: item["migrationStatus"]
            for item in self.catalog
            if item["migrationStatus"] not in {"Implemented", "BehaviorVerified"}
        }
        self.assertEqual({}, incomplete)

    def test_every_catalog_command_has_an_explicit_executor_route(self) -> None:
        catalog_read_only = {item["id"] for item in self.catalog if item["readOnly"]}
        catalog_mutating = {item["id"] for item in self.catalog if not item["readOnly"]}
        source = (ROOT / "src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.cs").read_text(encoding="utf-8")
        read_start = source.index("private async Task<CommandHandlerOutcome> ExecuteReadOnlyAsync")
        mutation_start = source.index("private async Task<CommandHandlerOutcome> ExecuteMutationAsync")
        validation_start = source.index("private static void ValidateMutationParameters")
        read_routes = set(re.findall(r'"([a-z0-9-]+)"\s*=>', source[read_start:mutation_start])) | {"catalog", "presets"}
        mutation_routes = set(re.findall(r'"([a-z0-9-]+)"\s*=>', source[mutation_start:validation_start]))
        validation_routes = set(re.findall(r'case\s+"([a-z0-9-]+)"', source[validation_start:]))
        self.assertEqual(catalog_read_only, read_routes)
        self.assertEqual(catalog_mutating, mutation_routes)
        self.assertEqual(catalog_mutating, validation_routes)

    def test_runtime_uses_executor_boundary_instead_of_copy_pasted_handlers(self) -> None:
        required = [
            ROOT / "src/WinCare.Application/Commands/ICommandOperationExecutor.cs",
            ROOT / "src/WinCare.Application/Commands/DelegatingCommandHandler.cs",
            ROOT / "src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.cs",
            ROOT / "src/WinCare.Infrastructure/Commands/BoundedProcessRunner.cs",
            ROOT / "src/WinCare.Infrastructure/Commands/CommandStateStore.cs",
        ]
        self.assertEqual([], [str(path.relative_to(ROOT)) for path in required if not path.is_file()])

    def test_generated_fake_success_handlers_are_removed(self) -> None:
        offenders: list[str] = []
        banned_literals = (
            "Windows updates installed successfully",
            "installedUpdatesCount = 2",
            "overallHealthScore = 98",
            'new { name = "Microsoft Visual Studio 2022"',
            "receiveWindowAutoTuningLevel = \"normal\"",
        )
        for path in HANDLERS.glob("*.cs"):
            text = path.read_text(encoding="utf-8")
            if any(literal in text for literal in banned_literals):
                offenders.append(path.name)
        self.assertEqual([], offenders)

    def test_no_json_element_is_passed_as_success_message(self) -> None:
        offenders: list[str] = []
        pattern = re.compile(r"Succeeded\(\s*\"[^\"]+\"\s*,\s*payload\s*\)")
        for path in (ROOT / "src").rglob("*.cs"):
            if pattern.search(path.read_text(encoding="utf-8")):
                offenders.append(str(path.relative_to(ROOT)))
        self.assertEqual([], offenders)

    def test_tool_execution_exposes_parameter_json_and_validates_it(self) -> None:
        vm = (ROOT / "src/WinCare.App/ViewModels/Pages/ToolExecutionViewModel.cs").read_text(encoding="utf-8")
        xaml = (ROOT / "src/WinCare.App/Views/Pages/AllToolsPage.xaml").read_text(encoding="utf-8")
        self.assertIn("ParameterJson", vm)
        self.assertIn("command.parameters_json_invalid", vm)
        self.assertIn("Execution.ParameterJson", xaml)

    def test_activity_runtime_has_no_last_journal_service_locator(self) -> None:
        runtime = (ROOT / "src/WinCare.Application/Commands/CommandRuntime.cs").read_text(encoding="utf-8")
        activity = (ROOT / "src/WinCare.Application/Activity/ActivityJournalService.cs").read_text(encoding="utf-8")
        self.assertNotIn("LastJournal", runtime)
        self.assertIn("IActivityJournalService", activity)

    def test_native_runtime_roots_contain_no_powershell(self) -> None:
        native_roots = (
            ROOT / "src/WinCare.App",
            ROOT / "src/WinCare.Application",
            ROOT / "src/WinCare.CommandCatalog",
            ROOT / "src/WinCare.Domain",
            ROOT / "src/WinCare.Infrastructure",
            ROOT / "native",
        )
        powershell = [
            str(path.relative_to(ROOT))
            for root in native_roots
            for path in root.rglob("*")
            if path.is_file() and path.suffix.lower() in {".ps1", ".psm1", ".psd1"}
        ]
        self.assertEqual([], powershell)

    def test_native_executor_has_no_unbounded_recursive_file_walks_or_fake_undo(self) -> None:
        text = "\n".join(
            path.read_text(encoding="utf-8")
            for path in (ROOT / "src/WinCare.Infrastructure/Commands").glob("WindowsCommandExecutor*.cs")
        )
        self.assertNotIn("SearchOption.AllDirectories", text)
        self.assertNotIn("recursive: true", text)
        self.assertNotIn("undo: true", text)
        self.assertIn("AttributesToSkip = FileAttributes.ReparsePoint", text)
        self.assertIn("is a reparse point and is not admitted as an operation root", text)

    def test_file_preview_is_bounded_instead_of_reading_entire_file(self) -> None:
        experience = (ROOT / "src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Experience.cs").read_text(encoding="utf-8")
        self.assertNotIn("File.ReadAllBytes", experience)
        self.assertIn("Math.Min(info.Length, maxBytes)", experience)

    def test_activity_page_refreshes_cached_journal_on_navigation(self) -> None:
        codebehind = (ROOT / "src/WinCare.App/Views/Pages/ActivityPage.xaml.cs").read_text(encoding="utf-8")
        self.assertIn("OnNavigatedTo", codebehind)
        self.assertIn("ViewModel.RefreshFromJournal()", codebehind)

    def test_viewmodels_do_not_depend_on_xaml_types(self) -> None:
        offenders = []
        for path in (ROOT / "src/WinCare.App/ViewModels").rglob("*.cs"):
            if "Microsoft.UI.Xaml" in path.read_text(encoding="utf-8"):
                offenders.append(str(path.relative_to(ROOT)))
        self.assertEqual([], offenders)
        converter = ROOT / "src/WinCare.App/Converters/ThemeResourceBrushConverter.cs"
        self.assertTrue(converter.is_file())

    def test_security_maintenance_never_disables_firewall_as_a_substitute_control(self) -> None:
        security = (ROOT / "src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Security.cs").read_text(encoding="utf-8")
        self.assertNotIn('["advfirewall", "set", "allprofiles", "state", "off"]', security)
        for control in ("DefenderRealtime", "SmartScreenShell", "Uac", "WindowsUpdateStack"):
            self.assertIn(control, security)
        self.assertIn("No host state was changed", security)
        self.assertIn("authenticated automatic restoration", security)

    def test_firewall_state_uses_registry_profiles_not_localized_netsh_text(self) -> None:
        system = (ROOT / "src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.System.cs").read_text(encoding="utf-8")
        self.assertIn("ReadFirewallProfileStates", system)
        self.assertIn("DomainProfile", system)
        self.assertIn("StandardProfile", system)
        self.assertIn("PublicProfile", system)
        self.assertNotIn('Contains("ON"', system)


    def test_command_input_is_strictly_typed_and_bounded(self) -> None:
        parameters = (ROOT / "src/WinCare.Infrastructure/Commands/CommandParameters.cs").read_text(encoding="utf-8")
        vm = (ROOT / "src/WinCare.App/ViewModels/Pages/ToolExecutionViewModel.cs").read_text(encoding="utf-8")
        self.assertIn("MaxStringCharacters", parameters)
        self.assertIn("MaxArrayItems", parameters)
        self.assertIn("must be a string", parameters)
        self.assertNotIn(": value.ToString();", parameters)
        self.assertIn("MaxParameterJsonCharacters", vm)
        self.assertIn("parameter JSON exceeds", vm)

    def test_long_running_commands_do_not_share_the_old_30_second_deadline(self) -> None:
        vm = (ROOT / "src/WinCare.App/ViewModels/Pages/ToolExecutionViewModel.cs").read_text(encoding="utf-8")
        self.assertIn("ExecutionBudget", vm)
        self.assertIn('"wua-install"', vm)
        self.assertIn('"download-start"', vm)
        self.assertIn("TimeSpan.FromHours", vm)
        self.assertNotIn("DateTimeOffset.UtcNow.AddSeconds(30)", vm)

    def test_durable_append_rejects_duplicate_ids(self) -> None:
        state = (ROOT / "src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.State.cs").read_text(encoding="utf-8")
        self.assertIn("State item", state)
        self.assertIn("already exists", state)
        self.assertIn("TryGetProperty(\"id\"", state)

    def test_application_layer_does_not_reference_platform_packages(self) -> None:
        project = (ROOT / "src/WinCare.Application/WinCare.Application.csproj").read_text(encoding="utf-8")
        self.assertNotIn("Microsoft.Win32.Registry", project)
        self.assertNotIn("System.Diagnostics.EventLog", project)

    def test_process_cancellation_observes_output_reader_tasks(self) -> None:
        runner = (ROOT / "src/WinCare.Infrastructure/Commands/BoundedProcessRunner.cs").read_text(encoding="utf-8")
        self.assertIn("ObserveReaderCompletionAsync", runner)
        self.assertIn("await ObserveReaderCompletionAsync(stdout, stderr)", runner)


    def test_viewmodels_have_no_async_void_workflows(self) -> None:
        offenders = []
        for path in (ROOT / "src/WinCare.App/ViewModels").rglob("*.cs"):
            if re.search(r"\basync\s+void\b", path.read_text(encoding="utf-8")):
                offenders.append(str(path.relative_to(ROOT)))
        self.assertEqual([], offenders)

    def test_process_based_cleanup_and_rollback_are_not_silently_swallowed(self) -> None:
        security = (ROOT / "src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Security.cs").read_text(encoding="utf-8")
        productivity = (ROOT / "src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Productivity.cs").read_text(encoding="utf-8")
        self.assertIn("stopResult", security)
        self.assertIn("rollbackResult", productivity)
        self.assertNotIn('try { await _process.RunAsync("logman.exe", ["stop", session, "-ets"], CancellationToken.None, TimeSpan.FromSeconds(15)).ConfigureAwait(false); } catch { }', security)
        self.assertNotIn('try { await _process.RunAsync(viveTool, [opposite, ids], CancellationToken.None, TimeSpan.FromMinutes(2)).ConfigureAwait(false); } catch { }', productivity)


    def test_windows_update_preflights_all_ids_before_mutating(self) -> None:
        security = (ROOT / "src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Security.cs").read_text(encoding="utf-8")
        self.assertIn("string[] requested = ids.Distinct", security)
        self.assertIn("var candidates = new List<dynamic>()", security)
        hide_method = security[security.index("WindowsUpdateSetHiddenAsync"):security.index("WindowsUpdateDownloadAsync")]
        self.assertLess(hide_method.index("missing"), hide_method.index("update.IsHidden = hidden"))
        select_method = security[security.index("private static dynamic SelectUpdates"):security.index("private async Task<CommandHandlerOutcome> WdacPoliciesAsync")]
        self.assertLess(select_method.index("missing"), select_method.index("AcceptEula"))

    def test_bounded_process_runner_prefers_system32_for_windows_tools(self) -> None:
        runner = (ROOT / "src/WinCare.Infrastructure/Commands/BoundedProcessRunner.cs").read_text(encoding="utf-8")
        self.assertIn("ResolveExecutable", runner)
        self.assertIn("Environment.SpecialFolder.System", runner)
        self.assertIn("FileName = ResolveExecutable(fileName)", runner)

    def test_security_hardening_reports_partial_application_as_failure(self) -> None:
        security = (ROOT / "src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Security.cs").read_text(encoding="utf-8")
        self.assertNotIn('if (firewall.ExitCode != 0) return Block("hardening-apply"', security)
        self.assertNotIn('if (hypervisor.ExitCode != 0) return Block("vbs-harden"', security)
        self.assertIn("hardening-apply.partial_failure", security)
        self.assertIn("vbs-harden.partial_failure", security)

    def test_studio_external_tools_are_hash_pinned_and_argument_bounded(self) -> None:
        productivity = (ROOT / "src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Productivity.cs").read_text(encoding="utf-8")
        self.assertIn('["devices", "-l"]', productivity)
        self.assertIn('p.RequiredString("Sha256")', productivity)
        self.assertNotIn('p.StringArray("Arguments")', productivity)
        self.assertIn('/id:52580392,50902630', productivity)
        self.assertIn("WezTerm", productivity)


if __name__ == "__main__":
    unittest.main()
