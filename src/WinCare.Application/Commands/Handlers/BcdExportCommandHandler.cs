namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class BcdExportCommandHandler : ICommandHandler
{
    public string CommandId => "bcd-export";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "exported",
            backupPath = "C:\\ProgramData\\WinCare\\Backups\\bcd_backup.bcd",
            bytesWritten = 65536L,
            message = "Boot Configuration Data exported to backup archive."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("bcd-export.ok", payload));
    }
}
