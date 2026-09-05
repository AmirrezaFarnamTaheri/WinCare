using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Tests;

public sealed class CommandDispatcherTests
{
    [Fact]
    public async Task Duplicate_dynamic_registration_cannot_replace_policy_or_handler()
    {
        var dispatcher = CreateDispatcher([], []);
        var original = new RecordingHandler("plugin.unique");
        var replacement = new RecordingHandler("plugin.unique");
        Assert.True(dispatcher.RegisterDynamicCommand(Definition("plugin.unique", MigrationStatus.Implemented, true), original));
        Assert.False(dispatcher.RegisterDynamicCommand(Definition("plugin.unique", MigrationStatus.Implemented, false), replacement));
        var result = await dispatcher.ExecuteAsync(Request("plugin.unique"), CommandExecutionOptions.Default, default);
        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal(1, original.InvocationCount);
        Assert.Equal(0, replacement.InvocationCount);
    }

    [Fact]
    public async Task Unknown_command_is_blocked_without_invoking_a_handler()
    {
        CommandDispatcher dispatcher = CreateDispatcher([], []);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("missing"), CommandExecutionOptions.Default, CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, result.Status);
        Assert.Equal("command.not_found", result.Code);
    }

    [Fact]
    public async Task Cataloged_command_is_reported_as_not_migrated()
    {
        CommandDefinition definition = Definition("legacy", MigrationStatus.Cataloged, readOnly: true);
        CommandDispatcher dispatcher = CreateDispatcher([definition], []);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("legacy"), CommandExecutionOptions.Default, CancellationToken.None);

        Assert.Equal(CommandResultStatus.NotMigrated, result.Status);
        Assert.Equal("command.migration_blocked", result.Code);
    }

    [Fact]
    public async Task Parameters_must_be_a_json_object()
    {
        CommandDefinition definition = Definition("read", MigrationStatus.Implemented, readOnly: true);
        RecordingHandler handler = new("read");
        CommandDispatcher dispatcher = CreateDispatcher([definition], [handler]);
        CommandRequest request = new(
            "read",
            JsonSerializer.SerializeToElement(new[] { "not", "an", "object" }),
            Apply: false,
            Guid.NewGuid());

        CommandResult result = await dispatcher.ExecuteAsync(
            request, CommandExecutionOptions.Default, CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, result.Status);
        Assert.Equal("command.parameters_invalid", result.Code);
        Assert.Equal(request.CorrelationId, result.CorrelationId);
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public async Task Apply_is_rejected_for_a_read_only_command()
    {
        CommandDefinition definition = Definition("read", MigrationStatus.Implemented, readOnly: true);
        RecordingHandler handler = new("read");
        CommandDispatcher dispatcher = CreateDispatcher([definition], [handler]);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("read", apply: true), CommandExecutionOptions.Default, CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, result.Status);
        Assert.Equal("command.readonly_mutation_denied", result.Code);
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public async Task Mutating_command_requires_reviewed_approval()
    {
        CommandDefinition definition = Definition("change", MigrationStatus.Implemented, readOnly: false);
        RecordingHandler handler = new("change");
        CommandDispatcher dispatcher = CreateDispatcher([definition], [handler]);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("change", apply: true),
            new CommandExecutionOptions(ReviewApproved: false, Deadline: null),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, result.Status);
        Assert.Equal("command.review_required", result.Code);
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public async Task Caller_created_approval_without_dispatcher_preview_is_blocked()
    {
        CommandDefinition definition = Definition("change", MigrationStatus.Implemented, readOnly: false);
        RecordingHandler handler = new("change");
        CommandDispatcher dispatcher = CreateDispatcher([definition], [handler]);
        JsonElement parameters = JsonSerializer.SerializeToElement(new { value = 1 });
        Guid correlationId = Guid.NewGuid();
        ApprovedMutationPlan forged = ApprovedMutationPlan.Create("change", parameters, correlationId);

        CommandResult result = await dispatcher.ExecuteAsync(
            new CommandRequest("change", parameters, Apply: true, correlationId, forged),
            new CommandExecutionOptions(ReviewApproved: true),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, result.Status);
        Assert.Equal("command.approval_plan_invalid", result.Code);
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public async Task Successful_mutation_preview_issues_single_use_review_plan()
    {
        CommandDefinition definition = Definition("change", MigrationStatus.Implemented, readOnly: false);
        RecordingHandler handler = new("change");
        CommandDispatcher dispatcher = CreateDispatcher([definition], [handler]);
        JsonElement parameters = JsonSerializer.SerializeToElement(new { value = 1 });

        CommandResult preview = await dispatcher.ExecuteAsync(
            CommandRequest.Preview("change", parameters),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, preview.Status);
        Assert.NotNull(preview.ReviewPlan);

        CommandResult applied = await dispatcher.ExecuteAsync(
            CommandRequest.Execute("change", parameters, preview.ReviewPlan),
            new CommandExecutionOptions(ReviewApproved: true),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, applied.Status);
        Assert.Equal(2, handler.InvocationCount);

        CommandResult replay = await dispatcher.ExecuteAsync(
            CommandRequest.Execute("change", parameters, preview.ReviewPlan),
            new CommandExecutionOptions(ReviewApproved: true),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, replay.Status);
        Assert.Equal("command.approval_plan_invalid", replay.Code);
        Assert.Equal(2, handler.InvocationCount);
    }

    [Fact]
    public async Task Review_plan_is_bound_to_exact_preview_parameters()
    {
        CommandDefinition definition = Definition("change", MigrationStatus.Implemented, readOnly: false);
        RecordingHandler handler = new("change");
        CommandDispatcher dispatcher = CreateDispatcher([definition], [handler]);
        JsonElement previewParameters = JsonSerializer.SerializeToElement(new { value = 1 });
        JsonElement changedParameters = JsonSerializer.SerializeToElement(new { value = 2 });

        CommandResult preview = await dispatcher.ExecuteAsync(
            CommandRequest.Preview("change", previewParameters),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.NotNull(preview.ReviewPlan);
        CommandRequest tampered = new(
            "change",
            changedParameters,
            Apply: true,
            preview.CorrelationId,
            preview.ReviewPlan);

        CommandResult result = await dispatcher.ExecuteAsync(
            tampered,
            new CommandExecutionOptions(ReviewApproved: true),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, result.Status);
        Assert.Equal("command.approval_plan_invalid", result.Code);
        Assert.Equal(1, handler.InvocationCount);
    }

    [Fact]
    public async Task Expired_deadline_cancels_before_handler_invocation()
    {
        CommandDefinition definition = Definition("read", MigrationStatus.Implemented, readOnly: true);
        RecordingHandler handler = new("read");
        CommandDispatcher dispatcher = CreateDispatcher([definition], [handler]);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("read"),
            new CommandExecutionOptions(ReviewApproved: false, Deadline: DateTimeOffset.UtcNow.AddSeconds(-1)),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Cancelled, result.Status);
        Assert.Equal("command.deadline_exceeded", result.Code);
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public async Task Far_future_deadline_does_not_overflow_cancel_after()
    {
        CommandDefinition definition = Definition("read", MigrationStatus.Implemented, readOnly: true);
        RecordingHandler handler = new("read");
        CommandDispatcher dispatcher = CreateDispatcher([definition], [handler]);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("read"),
            new CommandExecutionOptions(ReviewApproved: false, Deadline: DateTimeOffset.UtcNow.AddDays(30)),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("test.succeeded", result.Code);
        Assert.Equal(1, handler.InvocationCount);
    }

    [Fact]
    public async Task Precancelled_token_cancels_before_handler_invocation()
    {
        CommandDefinition definition = Definition("read", MigrationStatus.Implemented, readOnly: true);
        RecordingHandler handler = new("read");
        CommandDispatcher dispatcher = CreateDispatcher([definition], [handler]);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("read"), CommandExecutionOptions.Default, cancellation.Token);

        Assert.Equal(CommandResultStatus.Cancelled, result.Status);
        Assert.Equal("command.cancelled", result.Code);
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public async Task Default_runtime_routes_catalog_commands_through_the_injected_executor()
    {
        RecordingExecutor executor = new();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);

        CommandRequest request = Request("catalog");
        CommandResult result = await dispatcher.ExecuteAsync(
            request, CommandExecutionOptions.Default, CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal(request.CorrelationId, result.CorrelationId);
        Assert.Equal("executor.succeeded", result.Code);
        Assert.Equal("catalog", executor.LastDefinitionId);
        Assert.Equal(1, executor.InvocationCount);
    }

    [Fact]
    public async Task Default_runtime_registers_all_stable_catalog_commands()
    {
        RecordingExecutor executor = new();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("presets"), CommandExecutionOptions.Default, CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("presets", executor.LastDefinitionId);
        Assert.Equal(1, executor.InvocationCount);
    }

    [Fact]
    public void Duplicate_handler_ids_are_rejected()
    {
        CommandDefinition definition = Definition("read", MigrationStatus.Implemented, readOnly: true);

        Assert.Throws<ArgumentException>(() => CreateDispatcher(
            [definition], [new RecordingHandler("read"), new RecordingHandler("read")]));
    }

    [Fact]
    public async Task Dynamic_command_registration_and_execution_succeeds()
    {
        CommandDispatcher dispatcher = CreateDispatcher([], []);
        CommandDefinition dynamicDef = Definition("plugin.custom_clean", MigrationStatus.Implemented, readOnly: true);
        RecordingHandler dynamicHandler = new("plugin.custom_clean");

        bool registered = dispatcher.RegisterDynamicCommand(dynamicDef, dynamicHandler);
        Assert.True(registered);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("plugin.custom_clean"), CommandExecutionOptions.Default, CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal(1, dynamicHandler.InvocationCount);

        bool unregistered = dispatcher.UnregisterDynamicCommand("plugin.custom_clean");
        Assert.True(unregistered);

        CommandResult afterResult = await dispatcher.ExecuteAsync(
            Request("plugin.custom_clean"), CommandExecutionOptions.Default, CancellationToken.None);
        Assert.Equal(CommandResultStatus.Blocked, afterResult.Status);
        Assert.Equal("command.not_found", afterResult.Code);
    }

    [Fact]
    public void Dynamic_registration_fails_closed_on_core_namespace_collision()
    {
        CommandDefinition coreDef = Definition("core_cmd", MigrationStatus.Implemented, readOnly: true);
        RecordingHandler coreHandler = new("core_cmd");
        CommandDispatcher dispatcher = CreateDispatcher([coreDef], [coreHandler]);

        // Attempt to overwrite existing core command
        bool overwriteAttempt = dispatcher.RegisterDynamicCommand(
            Definition("core_cmd", MigrationStatus.Implemented, readOnly: true),
            new RecordingHandler("core_cmd"));
        Assert.False(overwriteAttempt);

        // Attempt to register in reserved wincare.core.* namespace
        bool reservedAttempt = dispatcher.RegisterDynamicCommand(
            Definition("wincare.core.critical_tool", MigrationStatus.Implemented, readOnly: true),
            new RecordingHandler("wincare.core.critical_tool"));
        Assert.False(reservedAttempt);

        // Attempt to register in system.* namespace
        bool systemAttempt = dispatcher.RegisterDynamicCommand(
            Definition("system.reboot", MigrationStatus.Implemented, readOnly: true),
            new RecordingHandler("system.reboot"));
        Assert.False(systemAttempt);
    }

    private static CommandDispatcher CreateDispatcher(
        IReadOnlyList<CommandDefinition> definitions,
        IReadOnlyList<ICommandHandler> handlers) =>
        new(definitions, handlers, TimeProvider.System);

    private static CommandRequest Request(string id, bool apply = false) =>
        new(id, JsonSerializer.SerializeToElement(new { }), apply, Guid.NewGuid());

    private static CommandDefinition Definition(string id, MigrationStatus status, bool readOnly) =>
        new(
            id,
            id,
            "Test command",
            "All tools",
            "Commands",
            readOnly ? CommandRisk.ReadOnly : CommandRisk.Moderate,
            readOnly,
            AdministratorAccess.No,
            RestartExpectation.No,
            "test",
            status,
            [id]);

    private sealed class RecordingExecutor : ICommandOperationExecutor
    {
        public int InvocationCount { get; private set; }
        public string? LastDefinitionId { get; private set; }

        public Task<CommandHandlerOutcome> ExecuteAsync(
            CommandDefinition definition,
            CommandRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;
            LastDefinitionId = definition.Id;
            return Task.FromResult(CommandHandlerOutcome.Succeeded(
                "executor.succeeded",
                "Executor invoked.",
                JsonSerializer.SerializeToElement(new { definition.Id })));
        }
    }

    private sealed class RecordingHandler(string commandId) : ICommandHandler
    {
        public string CommandId { get; } = commandId;
        public int InvocationCount { get; private set; }

        public Task<CommandHandlerOutcome> ExecuteAsync(
            CommandRequest request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult(CommandHandlerOutcome.Succeeded(
                "test.succeeded",
                "Test command succeeded.",
                JsonSerializer.SerializeToElement(new { ok = true })));
        }
    }
}
