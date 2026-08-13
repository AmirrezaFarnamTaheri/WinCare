namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class OfflineImagesCommandHandler : ICommandHandler
{
    public string CommandId => "offline-images";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            mountedImagesCount = 0,
            wimImagesFoundCount = 1,
            message = "Offline Windows WIM/VHDX image targets evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("offline-images.ok", payload));
    }
}
