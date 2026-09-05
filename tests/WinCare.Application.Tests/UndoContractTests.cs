using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;
using Xunit;

namespace WinCare.Application.Tests;

public sealed class UndoContractTests
{
    [Fact]
    public async Task Handler_undo_hint_is_not_exposed_without_dispatcher_compensator()
    {
        var definition = new CommandDefinition(
            "change",
            "Change",
            "Mutation",
            "All tools",
            "Commands",
            CommandRisk.Moderate,
            ReadOnly: false,
            AdministratorAccess.No,
            RestartExpectation.No,
            "test",
            MigrationStatus.Implemented,
            ["change"]);
        var dispatcher = new CommandDispatcher([definition], [new UndoHintHandler()]);
        JsonElement parameters = JsonSerializer.SerializeToElement(new { value = 1 });

        CommandResult preview = await dispatcher.ExecuteAsync(
            CommandRequest.Preview("change", parameters),
            CommandExecutionOptions.Default,
            CancellationToken.None);
        Assert.NotNull(preview.ReviewPlan);

        CommandResult applied = await dispatcher.ExecuteAsync(
            CommandRequest.Execute("change", parameters, preview.ReviewPlan),
            new CommandExecutionOptions(ReviewApproved: true),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, applied.Status);
        Assert.False(applied.UndoAvailable);
    }

    private sealed class UndoHintHandler : ICommandHandler
    {
        public string CommandId => "change";

        public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(CommandHandlerOutcome.Succeeded(
                "change.ok",
                request.Apply ? "Applied." : "Previewed.",
                undoAvailable: true));
    }
}
