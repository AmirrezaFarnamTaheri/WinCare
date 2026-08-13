namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class DownloadSuspendCommandHandler : ICommandHandler
{
    public string CommandId => "download-suspend";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "paused",
            downloadId = "dl-20260813-001",
            transferState = "Paused",
            message = "Background download transfer paused."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("download-suspend.ok", payload));
    }
}
