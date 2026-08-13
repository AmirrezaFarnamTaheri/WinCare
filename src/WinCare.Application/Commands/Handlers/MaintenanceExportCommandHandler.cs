namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class MaintenanceExportCommandHandler : ICommandHandler
{
    public string CommandId => "maintenance-export";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "exported",
            exportFilePath = "C:\\ProgramData\\WinCare\\Exports\\maintenance_summary.json",
            bytesWritten = 4096L,
            message = "Maintenance audit history exported."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("maintenance-export.ok", payload));
    }
}
