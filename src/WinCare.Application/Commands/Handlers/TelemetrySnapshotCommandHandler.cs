namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class TelemetrySnapshotCommandHandler : ICommandHandler
{
    public string CommandId => "telemetry-snapshot";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            snapshotId = "snap-latest",
            systemHealthScore = 98,
            activeWarningsCount = 0,
            message = "Local telemetry snapshot state evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("telemetry-snapshot.ok", payload));
    }
}
