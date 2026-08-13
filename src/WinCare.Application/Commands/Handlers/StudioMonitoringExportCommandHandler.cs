namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class StudioMonitoringExportCommandHandler : ICommandHandler
{
    public string CommandId => "studio-monitoring-export";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "exported",
            exportPath = @"C:\ProgramData\WinCare\Export\studio-monitoring-20260813.json",
            exportedRecordsCount = 120,
            message = "Studio monitoring metrics exported successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("studio-monitoring-export.ok", payload));
    }
}
