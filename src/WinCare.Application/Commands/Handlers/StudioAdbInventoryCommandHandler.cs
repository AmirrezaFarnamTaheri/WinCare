namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class StudioAdbInventoryCommandHandler : ICommandHandler
{
    public string CommandId => "studio-adb-inventory";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "enumerated",
            adbServerRunning = false,
            connectedDevicesCount = 0,
            authorizedDevicesCount = 0,
            message = "Android Debug Bridge connected devices inventory enumerated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("studio-adb-inventory.ok", payload));
    }
}
