namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class MaintenanceTransitionCommandHandler : ICommandHandler
{
    public string CommandId => "maintenance-transition";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "transitioned",
            previousState = "Disabled",
            newState = "Enabled",
            message = "Maintenance window state transitioned to Enabled."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("maintenance-transition.ok", payload));
    }
}
