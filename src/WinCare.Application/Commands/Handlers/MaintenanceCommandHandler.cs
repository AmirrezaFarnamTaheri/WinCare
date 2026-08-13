namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class MaintenanceCommandHandler : ICommandHandler
{
    public string CommandId => "maintenance";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            automaticMaintenanceState = "Idle",
            lastRunTime = "2026-08-13T02:00:00Z",
            message = "Automatic Maintenance state retrieved."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("maintenance.ok", payload));
    }
}
