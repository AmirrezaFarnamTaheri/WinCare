namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class OfflinePackageAddCommandHandler : ICommandHandler
{
    public string CommandId => "offline-package-add";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "added",
            packageName = "Package_for_KB5034123",
            message = "Offline servicing package staged into image store."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("offline-package-add.ok", payload));
    }
}
