using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Tests;

/// <summary>
/// The documentation capture session's read-only contract is enforced at runtime by
/// CaptureModeCommandDispatcher, not by scanning source for the string "ExecuteAsync".
/// These tests pin that behaviour: a dispatch is rejected and recorded, while plugin
/// registration still reaches the real dispatcher.
/// </summary>
public sealed class CaptureModeCommandDispatcherTests
{
    [Fact]
    public async Task Execute_rejects_instead_of_reaching_the_inner_dispatcher()
    {
        var inner = new RecordingDispatcher();
        ICommandDispatcher capture = new CaptureModeCommandDispatcher(inner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            capture.ExecuteAsync(CommandRequest.Preview("system"), CommandExecutionOptions.Default, CancellationToken.None));

        Assert.Contains("documentation capture", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("system", ex.Message);
        Assert.Equal(0, inner.ExecutionCount);
    }

    [Fact]
    public async Task Rejected_requests_are_recorded_so_a_run_can_report_them()
    {
        var capture = new CaptureModeCommandDispatcher(new RecordingDispatcher());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            capture.ExecuteAsync(CommandRequest.Preview("wua-search"), CommandExecutionOptions.Default, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            capture.ExecuteAsync(CommandRequest.Preview("disk.cleanup"), CommandExecutionOptions.Default, CancellationToken.None));

        Assert.Equal(2, capture.RejectedRequests.Count);
        Assert.Equal("wua-search", capture.RejectedRequests[0].CommandId);
        Assert.Equal("disk.cleanup", capture.RejectedRequests[1].CommandId);
    }

    [Fact]
    public async Task Rejection_records_every_request_even_when_the_caller_ignores_the_throw()
    {
        var capture = new CaptureModeCommandDispatcher(new RecordingDispatcher());

        for (int i = 0; i < 3; i++)
        {
            try { await capture.ExecuteAsync(CommandRequest.Preview("repeat"), CommandExecutionOptions.Default, CancellationToken.None); }
            catch (InvalidOperationException) { }
        }

        Assert.Equal(3, capture.RejectedRequests.Count);
    }

    [Fact]
    public void Dynamic_registration_still_reaches_the_inner_dispatcher()
    {
        var inner = new RecordingDispatcher();
        ICommandDispatcher capture = new CaptureModeCommandDispatcher(inner);
        CommandDefinition definition = Definition("plugin.tool");

        Assert.True(capture.RegisterDynamicCommand(definition, new RecordingHandler("plugin.tool")));
        Assert.Single(inner.RegisteredIds);
        Assert.Equal("plugin.tool", inner.RegisteredIds[0]);

        Assert.True(capture.UnregisterDynamicCommand("plugin.tool"));
        Assert.Empty(inner.RegisteredIds);
    }

    [Fact]
    public void Construction_rejects_a_null_inner_dispatcher()
    {
        Assert.Throws<ArgumentNullException>(() => new CaptureModeCommandDispatcher(null!));
    }

    private static CommandDefinition Definition(string commandId) => new(
        commandId,
        "Plugin tool",
        "summary",
        Area: "System care",
        Section: "Clean up",
        Risk: CommandRisk.ReadOnly,
        ReadOnly: true,
        AdministratorAccess: AdministratorAccess.No,
        Restart: RestartExpectation.No,
        LegacySource: string.Empty,
        MigrationStatus: MigrationStatus.Implemented,
        Keywords: Array.Empty<string>());

    private sealed class RecordingDispatcher : ICommandDispatcher
    {
        public int ExecutionCount;
        public readonly List<string> RegisteredIds = new();

        public Task<CommandResult> ExecuteAsync(
            CommandRequest request, CommandExecutionOptions options, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref ExecutionCount);
            return Task.FromResult(new CommandResult(
                request.CommandId,
                request.CorrelationId,
                CommandResultStatus.Succeeded,
                "ok",
                "recorded",
                Data: null,
                StartedAt: DateTimeOffset.UtcNow,
                CompletedAt: DateTimeOffset.UtcNow,
                UndoAvailable: false));
        }

        public bool RegisterDynamicCommand(CommandDefinition definition, ICommandHandler handler)
        {
            RegisteredIds.Add(definition.Id);
            return true;
        }

        public bool UnregisterDynamicCommand(string commandId) => RegisteredIds.Remove(commandId);
    }

    private sealed class RecordingHandler : ICommandHandler
    {
        public RecordingHandler(string commandId) => CommandId = commandId;
        public string CommandId { get; }
        public int InvocationCount;

        public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref InvocationCount);
            return Task.FromResult(CommandHandlerOutcome.Succeeded("ok", "recorded"));
        }
    }
}
