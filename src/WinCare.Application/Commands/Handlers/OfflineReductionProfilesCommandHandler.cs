namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class OfflineReductionProfilesCommandHandler : ICommandHandler
{
    public string CommandId => "offline-reduction-profiles";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            activeProfile = "balanced",
            availableProfiles = new[] { "minimal", "balanced", "aggressive" },
            message = "Offline image footprint reduction profiles evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("offline-reduction-profiles.ok", payload));
    }
}
