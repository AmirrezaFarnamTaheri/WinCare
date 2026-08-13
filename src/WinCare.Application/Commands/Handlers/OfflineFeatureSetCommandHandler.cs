namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class OfflineFeatureSetCommandHandler : ICommandHandler
{
    public string CommandId => "offline-feature-set";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "updated",
            featureName = "NetFx3",
            targetState = "Enabled",
            message = "Offline feature state updated in target WIM image."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("offline-feature-set.ok", payload));
    }
}
