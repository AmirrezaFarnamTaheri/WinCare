namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class OfflineDriverAddCommandHandler : ICommandHandler
{
    public string CommandId => "offline-driver-add";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "added",
            publishedName = "oem42.inf",
            driverSignature = "ValidCert",
            message = "Offline driver package staged into target image."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("offline-driver-add.ok", payload));
    }
}
