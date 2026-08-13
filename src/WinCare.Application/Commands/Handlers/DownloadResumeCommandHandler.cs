namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class DownloadResumeCommandHandler : ICommandHandler
{
    public string CommandId => "download-resume";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "resumed",
            downloadId = "dl-20260813-001",
            transferState = "Downloading",
            message = "Background download transfer resumed."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("download-resume.ok", payload));
    }
}
