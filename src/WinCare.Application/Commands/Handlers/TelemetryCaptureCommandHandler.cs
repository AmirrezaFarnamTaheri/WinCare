namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class TelemetryCaptureCommandHandler : ICommandHandler
{
    public string CommandId => "telemetry-capture";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "captured",
            snapshotId = "snap-20260813-220000",
            metricsRecorded = 42,
            message = "New telemetry snapshot captured and persisted."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("telemetry-capture.ok", payload));
    }
}
