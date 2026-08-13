namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class MaintenanceCreateCommandHandler : ICommandHandler
{
    public string CommandId => "maintenance-create";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "created",
            maintenanceWindowId = "MW-2026-0814",
            startTime = "02:00",
            durationMinutes = 60,
            message = "Maintenance window registered successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("maintenance-create.ok", payload));
    }
}
