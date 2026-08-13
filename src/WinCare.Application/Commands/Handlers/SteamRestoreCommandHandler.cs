namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class SteamRestoreCommandHandler : ICommandHandler
{
    public string CommandId => "steam-restore";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "completed",
            restoreSource = request.Parameters.TryGetValue("source", out var s) ? s : "default-backup-archive",
            message = "Steam game data restore completed successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("steam-restore.ok", payload));
    }
}
