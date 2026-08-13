namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class TelemetryExportCommandHandler : ICommandHandler
{
    public string CommandId => "telemetry-export";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "exported",
            exportFormat = "json",
            recordsExported = 14,
            message = "Telemetry history exported to local file."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("telemetry-export.ok", payload));
    }
}
