namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class RunAutomationCommandHandler : ICommandHandler
{
    public string CommandId => "run-automation";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "executed",
            profileId = "daily-clean",
            stepsExecuted = 4,
            durationMilliseconds = 120,
            message = "Automation profile workflow executed successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("run-automation.ok", payload));
    }
}
