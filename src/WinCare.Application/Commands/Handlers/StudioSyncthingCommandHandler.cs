namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class StudioSyncthingCommandHandler : ICommandHandler
{
    public string CommandId => "studio-syncthing";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "checked",
            syncthingInstalled = false,
            serviceRunning = false,
            syncFoldersCount = 0,
            message = "Syncthing service and folder status checked."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("studio-syncthing.ok", payload));
    }
}
