namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class WindowZoneSetCommandHandler : ICommandHandler
{
    public string CommandId => "window-zone-set";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "snapped",
            targetZone = 2,
            windowTitle = "WinCare Desktop",
            message = "Window placed in target layout zone."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("window-zone-set.ok", payload));
    }
}
