from __future__ import annotations

import json
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]


class CommandRuntimeTests(unittest.TestCase):
    def test_command_runtime_files_and_safe_handlers_exist(self) -> None:
        required = (
            "src/WinCare.Application/Commands/ICommandHandler.cs",
            "src/WinCare.Application/Commands/CommandHandlerOutcome.cs",
            "src/WinCare.Application/Commands/CommandExecutionOptions.cs",
            "src/WinCare.Application/Commands/CommandDispatcher.cs",
            "src/WinCare.Application/Commands/CommandRuntime.cs",
            "src/WinCare.Application/Commands/ICommandOperationExecutor.cs",
            "src/WinCare.Application/Commands/DelegatingCommandHandler.cs",
            "src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.cs",
            "src/WinCare.Infrastructure/Commands/BoundedProcessRunner.cs",
            "src/WinCare.Infrastructure/Commands/CommandStateStore.cs",
            "src/WinCare.App/Services/AppRuntime.cs",
            "src/WinCare.CommandCatalog/RemediationCatalog.cs",
            "src/WinCare.CommandCatalog/RemediationCatalogJsonContext.cs",
            "src/WinCare.CommandCatalog/EmbeddedJsonResource.cs",
            "src/WinCare.CommandCatalog/RemediationCatalogValidator.cs",
            "src/WinCare.App/ViewModels/Pages/ToolExecutionViewModel.cs",
            "src/WinCare.CommandCatalog/Data/remediation-rules.json",
            "src/WinCare.CommandCatalog/Data/presets.json",
        )
        missing = [relative for relative in required if not (ROOT / relative).is_file()]
        self.assertEqual([], missing)

    def test_all_catalog_commands_are_marked_implemented_or_behavior_verified(self) -> None:
        document = json.loads(
            (ROOT / "src/WinCare.CommandCatalog/Data/commands.json").read_text(encoding="utf-8")
        )
        commands = document["commands"]
        self.assertEqual(259, len(commands))
        self.assertEqual(259, len({command["id"] for command in commands}))
        incomplete = {
            command["id"]: command["migrationStatus"]
            for command in commands
            if command["migrationStatus"] not in {"Implemented", "BehaviorVerified"}
        }
        self.assertEqual({}, incomplete)

    def test_embedded_catalog_data_matches_the_frozen_oracle_data(self) -> None:
        pairs = (
            (
                "migration/oracle/remediation-rules.json",
                "src/WinCare.CommandCatalog/Data/remediation-rules.json",
            ),
            (
                "migration/oracle/presets.json",
                "src/WinCare.CommandCatalog/Data/presets.json",
            ),
        )
        for legacy, native in pairs:
            legacy_data = json.loads((ROOT / legacy).read_text(encoding="utf-8-sig"))
            native_data = json.loads((ROOT / native).read_text(encoding="utf-8"))
            self.assertEqual(legacy_data, native_data, native)

    def test_dispatcher_fails_closed_and_is_cancellation_aware(self) -> None:
        dispatcher = (ROOT / "src/WinCare.Application/Commands/CommandDispatcher.cs").read_text(
            encoding="utf-8"
        )
        for required in (
            "CommandResultStatus.NotMigrated",
            "command.not_found",
            "command.not_migrated",
            "command.readonly_mutation_denied",
            "request.CorrelationId",
            "command.review_required",
            "command.deadline_exceeded",
            "OperationCanceledException",
            "CancellationTokenSource.CreateLinkedTokenSource",
        ):
            self.assertIn(required, dispatcher)
        for banned in ("PowerShell", "pwsh", "Process.Start", "HttpClient"):
            self.assertNotIn(banned, dispatcher)

    def test_all_tools_binds_async_execution_and_xaml_is_valid(self) -> None:
        view_model = "\n".join(
            (ROOT / relative).read_text(encoding="utf-8")
            for relative in (
                "src/WinCare.App/ViewModels/Pages/AllToolsPageViewModel.cs",
                "src/WinCare.App/ViewModels/Pages/ToolExecutionViewModel.cs",
            )
        )
        xaml_path = ROOT / "src/WinCare.App/Views/Pages/AllToolsPage.xaml"
        xaml = xaml_path.read_text(encoding="utf-8")
        self.assertIn("ExecuteSelectedToolCommand", view_model)
        self.assertIn("correlationId = result.CorrelationId", view_model)
        self.assertIn("durationMilliseconds = result.Duration.TotalMilliseconds", view_model)
        self.assertIn("HasExecutionResult", view_model)
        self.assertIn("undoAvailable = result.UndoAvailable", view_model)
        self.assertIn("Command=\"{x:Bind ViewModel.Execution.ExecuteSelectedToolCommand}\"", xaml)
        self.assertIn("ExecutionResultText", xaml)
        self.assertIn("AutomationProperties.AutomationId=\"ExecuteSelectedTool\"", xaml)
        self.assertIn("AutomationProperties.AutomationId=\"CommandResultDetails\"", xaml)
        ET.fromstring(xaml)

    def test_review_safety_contracts_are_encoded_in_sources(self) -> None:
        native_interop = (ROOT / "src/WinCare.Infrastructure/Native/WinCareCoreNative.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn('EntryPoint = "wincare_core_dir_size"', native_interop)
        self.assertIn('EntryPoint = "wincare_core_sys_info"', native_interop)
        self.assertIn("WinCareCoreDirSize", native_interop)
        self.assertIn("WinCareCoreSysInfo", native_interop)

        executor = (ROOT / "src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.cs").read_text(
            encoding="utf-8"
        )
        for token in (
            "ValidateMutationParameters(definition.Id, p)",
            "if (!request.Apply)",
            "MutationPreview(definition, p)",
            "command.elevation_required",
            "UnauthorizedAccessException",
            "command.access_denied",
        ):
            self.assertIn(token, executor)

        tool_row = (ROOT / "src/WinCare.App/ViewModels/Pages/ToolRowViewModel.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn('"Read-only" => "PillReadOnlyBgBrush"', tool_row)
        self.assertIn('_ => "PillMutatingBgBrush"', tool_row)

        release_checklist = (ROOT / "tools/release_checklist.py").read_text(encoding="utf-8")
        self.assertIn("PYTHON = sys.executable", release_checklist)
        self.assertIn("sys.version_info < (3, 11)", release_checklist)

    def test_native_runtime_does_not_reference_legacy_runtime(self) -> None:
        native_roots = (
            ROOT / "src/WinCare.Application",
            ROOT / "src/WinCare.CommandCatalog",
            ROOT / "src/WinCare.Domain",
            ROOT / "src/WinCare.Infrastructure",
            ROOT / "src/WinCare.App",
            ROOT / "native/wincare-core",
        )
        banned = ("System.Management.Automation", "pwsh.exe", "powershell.exe", "UseWPF")
        findings: list[str] = []
        for native_root in native_roots:
            for path in native_root.rglob("*"):
                if not path.is_file() or path.suffix.lower() not in {".cs", ".xaml", ".rs", ".toml", ".json"}:
                    continue
                text = path.read_text(encoding="utf-8", errors="replace")
                for token in banned:
                    if token in text:
                        findings.append(f"{path.relative_to(ROOT)}: {token}")
        self.assertEqual([], findings)


if __name__ == "__main__":
    unittest.main()
