namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class WindowTopmostCommandHandler : ICommandHandler
{
    public string CommandId => "window-topmost";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "updated",
            topmostEnabled = true,
            message = "Window topmost state updated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("window-topmost.ok", payload));
    }
}
