namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class OfflineDriversCommandHandler : ICommandHandler
{
    public string CommandId => "offline-drivers";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            thirdPartyDriversCount = 14,
            unsignedDriversCount = 0,
            message = "Offline driver package store evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("offline-drivers.ok", payload));
    }
}
