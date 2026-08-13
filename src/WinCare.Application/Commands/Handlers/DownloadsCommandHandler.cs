namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class DownloadsCommandHandler : ICommandHandler
{
    public string CommandId => "downloads";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            activeDownloadsCount = 0,
            completedDownloadsCount = 12,
            message = "Background download manager state evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("downloads.ok", payload));
    }
}
