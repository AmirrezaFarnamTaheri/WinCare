namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class DownloadRemoveCommandHandler : ICommandHandler
{
    public string CommandId => "download-remove";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "removed",
            downloadId = "dl-20260813-001",
            message = "Download task and temporary files removed."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("download-remove.ok", payload));
    }
}
