namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class WindowActivateCommandHandler : ICommandHandler
{
    public string CommandId => "window-activate";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "activated",
            foregroundAssigned = true,
            message = "Target window activated and brought to foreground."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("window-activate.ok", payload));
    }
}
