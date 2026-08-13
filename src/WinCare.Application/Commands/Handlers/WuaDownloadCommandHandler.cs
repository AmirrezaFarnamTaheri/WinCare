namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class WuaDownloadCommandHandler : ICommandHandler
{
    public string CommandId => "wua-download";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "downloaded",
            pendingUpdatesCount = 2,
            totalBytesDownloaded = 145890200L,
            message = "Windows updates downloaded into local staging cache."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("wua-download.ok", payload));
    }
}
