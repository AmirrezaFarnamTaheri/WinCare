using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Tests;

public sealed class CommandDispatcherTests
{
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
