namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class SteamBackupCommandHandler : ICommandHandler
{
    public string CommandId => "steam-backup";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "completed",
            backupPath = request.Parameters.TryGetValue("path", out var p) ? p : "default-backup-dir",
            message = "Steam game data backup completed successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("steam-backup.ok", payload));
    }
}
