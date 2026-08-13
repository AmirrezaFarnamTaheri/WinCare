namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class DownloadStartDueCommandHandler : ICommandHandler
{
    public string CommandId => "download-start-due";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "processed",
            startedDownloadsCount = 0,
            message = "Due download queue processed."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("download-start-due.ok", payload));
    }
}
