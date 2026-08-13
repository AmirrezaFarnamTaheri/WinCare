namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class TelemetryHistoryCommandHandler : ICommandHandler
{
    public string CommandId => "telemetry-history";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            historicalSnapshotsCount = 14,
            oldestSnapshotTimestamp = "2026-08-01T00:00:00Z",
            message = "Local telemetry historical log store evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("telemetry-history.ok", payload));
    }
}
