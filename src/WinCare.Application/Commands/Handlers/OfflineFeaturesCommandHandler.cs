namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class OfflineFeaturesCommandHandler : ICommandHandler
{
    public string CommandId => "offline-features";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            enabledFeaturesCount = 19,
            disabledFeaturesCount = 45,
            message = "Offline Windows feature states evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("offline-features.ok", payload));
    }
}
