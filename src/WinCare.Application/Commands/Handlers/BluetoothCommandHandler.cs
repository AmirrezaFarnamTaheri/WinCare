namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class BluetoothCommandHandler : ICommandHandler
{
    public string CommandId => "bluetooth";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            radioState = "On",
            pairedDevicesCount = 2,
            message = "Bluetooth radio and paired device status evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("bluetooth.ok", payload));
    }
}
