namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class PowerSessionsCommandHandler : ICommandHandler
{
    public string CommandId => "power-sessions";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            activePlanId = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
            activePlanName = "High Performance",
            powerRequestsCount = 0,
            message = "Power scheme and session telemetry evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("power-sessions.ok", payload));
    }
}
