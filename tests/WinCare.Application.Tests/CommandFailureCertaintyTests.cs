using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
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
