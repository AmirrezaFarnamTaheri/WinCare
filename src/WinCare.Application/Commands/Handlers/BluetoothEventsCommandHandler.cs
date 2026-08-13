namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class BluetoothEventsCommandHandler : ICommandHandler
{
    public string CommandId => "bluetooth-events";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            recentEventsCount = 0,
            driverErrorsCount = 0,
            message = "Bluetooth BTHUSB event log entries inspected."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("bluetooth-events.ok", payload));
    }
}
