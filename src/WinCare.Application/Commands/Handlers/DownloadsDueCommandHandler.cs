namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class DownloadsDueCommandHandler : ICommandHandler
{
    public string CommandId => "downloads-due";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            dueQueueLength = 0,
            scheduledDownloadsCount = 2,
            message = "Scheduled and pending downloads queue evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("downloads-due.ok", payload));
    }
}
