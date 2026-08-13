namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class DownloadStartCommandHandler : ICommandHandler
{
    public string CommandId => "download-start";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "started",
            downloadId = "dl-20260813-001",
            transferState = "Downloading",
            message = "Background download transfer started."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("download-start.ok", payload));
    }
}
