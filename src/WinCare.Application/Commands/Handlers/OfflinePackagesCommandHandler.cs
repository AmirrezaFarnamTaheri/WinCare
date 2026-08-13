namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class OfflinePackagesCommandHandler : ICommandHandler
{
    public string CommandId => "offline-packages";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            packagesCount = 28,
            pendingUpdatesCount = 0,
            message = "Offline servicing packages enumerated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("offline-packages.ok", payload));
    }
}
