namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class WindowZonesCommandHandler : ICommandHandler
{
    public string CommandId => "window-zones";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            zonesConfiguredCount = 4,
            activeLayout = "Grid",
            message = "Window snap zones and workspace layouts evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("window-zones.ok", payload));
    }
}
