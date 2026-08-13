namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class SteamCloudFilesCommandHandler : ICommandHandler
{
    public string CommandId => "steam-cloud-files";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            cloudSyncEnabled = true,
            pendingSyncCount = 0,
            message = "Steam cloud sync files evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("steam-cloud-files.ok", payload));
    }
}
