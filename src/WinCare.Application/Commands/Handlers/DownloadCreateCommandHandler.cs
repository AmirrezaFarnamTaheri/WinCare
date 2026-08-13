namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class DownloadCreateCommandHandler : ICommandHandler
{
    public string CommandId => "download-create";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "created",
            downloadId = "dl-20260813-001",
            queued = true,
            message = "Download task queued in manager."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("download-create.ok", payload));
    }
}
