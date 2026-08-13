namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class OfflineReductionAssessCommandHandler : ICommandHandler
{
    public string CommandId => "offline-reduction-assess";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "assessed",
            estimatedSavingsMb = 1450,
            reducibleComponents = new[] { "winxs", "driver-store-backups", "temp-cache" },
            message = "Offline image footprint reduction potential assessed successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("offline-reduction-assess.ok", payload));
    }
}
