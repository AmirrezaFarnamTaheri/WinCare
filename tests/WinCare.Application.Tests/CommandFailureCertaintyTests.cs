using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Activity;
using WinCare.Domain.Commands;

namespace WinCare.Application.Tests;

public sealed class CommandFailureCertaintyTests
{
    [Fact]
    public async Task Mutating_handler_fault_reports_unknown_final_state()
    {
        CommandDefinition definition = new(
            "change",
            "Change",
            "Test mutation",
            "All tools",
            "Commands",
            CommandRisk.Moderate,
            ReadOnly: false,
            AdministratorAccess.No,
            RestartExpectation.No,
            "test",
            MigrationStatus.Implemented,
            ["change"]);
        var handler = new ThrowingHandler("change");
        var dispatcher = new CommandDispatcher([definition], [handler], TimeProvider.System);
        JsonElement parameters = JsonSerializer.SerializeToElement(new { value = 1 });

        CommandResult preview = await dispatcher.ExecuteAsync(
            CommandRequest.Preview("change", parameters),
            CommandExecutionOptions.Default,
            CancellationToken.None);
        Assert.NotNull(preview.ReviewPlan);

        handler.ThrowOnNextInvocation = true;
        CommandResult result = await dispatcher.ExecuteAsync(
            CommandRequest.Execute("change", parameters, preview.ReviewPlan),
            new CommandExecutionOptions(ReviewApproved: true),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Failed, result.Status);
        Assert.Equal("command.failed_state_unknown", result.Code);
        Assert.Contains("final system state is unknown", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancelled_mutation_reports_partial_effect_guidance()
    {
        CommandDefinition definition = new(
            "change",
            "Change",
            "Test mutation",
            "All tools",
            "Commands",
            CommandRisk.Moderate,
            ReadOnly: false,
            AdministratorAccess.No,
            RestartExpectation.No,
            "test",
            MigrationStatus.Implemented,
            ["change"]);
        var handler = new CancellingHandler("change");
        var dispatcher = new CommandDispatcher([definition], [handler], TimeProvider.System);
        JsonElement parameters = JsonSerializer.SerializeToElement(new { value = 1 });

        CommandResult preview = await dispatcher.ExecuteAsync(
            CommandRequest.Preview("change", parameters),
            CommandExecutionOptions.Default,
            CancellationToken.None);
        Assert.NotNull(preview.ReviewPlan);

        using CancellationTokenSource cancelledMidFlight = new();
        Task<CommandResult> execution = dispatcher.ExecuteAsync(
            CommandRequest.Execute("change", parameters, preview.ReviewPlan),
            new CommandExecutionOptions(ReviewApproved: true),
            cancelledMidFlight.Token);
        cancelledMidFlight.Cancel();
        CommandResult result = await execution;

        Assert.Equal(CommandResultStatus.Cancelled, result.Status);
        Assert.Equal("command.cancelled", result.Code);
        Assert.Contains("part of its work", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verify the affected system state", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CancellingHandler(string commandId) : ICommandHandler
    {
        public string CommandId { get; } = commandId;

        public async Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            if (request.Apply)
            {
                // Simulate work that is already in flight when cancellation arrives; the
                // await yields so the test can cancel while the handler is still running.
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            return CommandHandlerOutcome.Succeeded("preview.ok", "Preview succeeded.");
        }
    }

    private sealed class ThrowingHandler(string commandId) : ICommandHandler
    {
        public string CommandId { get; } = commandId;
        public bool ThrowOnNextInvocation { get; set; }

        public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            if (ThrowOnNextInvocation)
            {
                ThrowOnNextInvocation = false;
                throw new InvalidOperationException("simulated handler fault after admission");
            }
            return Task.FromResult(CommandHandlerOutcome.Succeeded("preview.ok", "Preview succeeded."));
        }
    }
}

/// <summary>
/// Regression tests for F-022: admission rejections were absent from the Activity journal.
/// A blocked admission must now produce a journal record carrying the rejection reason.
/// </summary>
public sealed class AdmissionJournalTests
{
    [Fact]
    public async Task Blocked_admission_is_journaled_with_its_reason()
    {
        CommandDefinition definition = new(
            "change",
            "Change",
            "Test mutation",
            "All tools",
            "Commands",
            CommandRisk.Moderate,
            ReadOnly: false,
            AdministratorAccess.No,
            RestartExpectation.No,
            "test",
            MigrationStatus.Implemented,
            ["change"]);
        var handler = new NoopHandler();
        var journal = new InMemoryJournal();
        var dispatcher = new CommandDispatcher([definition], [handler], journal: journal);
        JsonElement parameters = JsonSerializer.SerializeToElement(new { value = 1 });

        CommandResult result = await dispatcher.ExecuteAsync(
            new CommandRequest("change", parameters, Apply: true, Guid.NewGuid()),
            new CommandExecutionOptions(ReviewApproved: false),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, result.Status);
        ActivityRecord? record = Assert.Single(journal.Records);
        Assert.Equal(ActivityState.Failed, record.State);
        Assert.Contains("ReviewApproved", record.Result, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class NoopHandler : ICommandHandler
    {
        public string CommandId => "change";
        public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(CommandHandlerOutcome.Succeeded("change.ok", "Applied."));
    }

    private sealed class InMemoryJournal : WinCare.Application.Activity.IActivityJournalService
    {
        public List<ActivityRecord> Records { get; } = [];
        public event EventHandler? Changed { add { } remove { } }

        public ActivityRecord Begin(string commandId, string title)
        {
            var record = new ActivityRecord(Guid.NewGuid(), commandId, title, ActivityState.Running, DateTimeOffset.UtcNow, null, string.Empty, false);
            Records.Add(record);
            return record;
        }

        public IReadOnlyList<ActivityRecord> GetAll() => Records.ToArray();
        public bool IsPersistenceHealthy => true;
        public string? PersistenceStatusMessage => null;

        public void Complete(Guid id, string result, bool undoAvailable = false) => Transition(id, ActivityState.Completed, result);
        public void Fail(Guid id, string result) => Transition(id, ActivityState.Failed, result);
        public void Cancel(Guid id) => Transition(id, ActivityState.Cancelled, "Cancelled by user.");
        public void RequireAttention(Guid id, string message) => Transition(id, ActivityState.NeedsAttention, message);
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        private void Transition(Guid id, ActivityState state, string result)
        {
            int index = Records.FindIndex(r => r.Id == id);
            if (index >= 0)
            {
                Records[index] = Records[index] with { State = state, Result = result, CompletedAt = DateTimeOffset.UtcNow };
            }
        }
    }
}
