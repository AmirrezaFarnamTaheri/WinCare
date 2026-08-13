namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class DownloadCancelCommandHandler : ICommandHandler
{
    public string CommandId => "download-cancel";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "cancelled",
            downloadId = "dl-20260813-001",
            message = "Background download transfer cancelled."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("download-cancel.ok", payload));
    }
}
