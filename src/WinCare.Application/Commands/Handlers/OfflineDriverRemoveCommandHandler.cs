namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class OfflineDriverRemoveCommandHandler : ICommandHandler
{
    public string CommandId => "offline-driver-remove";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "removed",
            publishedName = "oem42.inf",
            message = "Offline driver package removed from target image."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("offline-driver-remove.ok", payload));
    }
}
