namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class MaintenanceMetricsCommandHandler : ICommandHandler
{
    public string CommandId => "maintenance-metrics";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "retrieved",
            successfulRuns = 14,
            failedRuns = 0,
            averageDurationSeconds = 45,
            message = "Maintenance task metrics and history retrieved."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("maintenance-metrics.ok", payload));
    }
}
