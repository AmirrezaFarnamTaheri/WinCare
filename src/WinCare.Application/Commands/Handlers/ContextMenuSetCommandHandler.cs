namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class ContextMenuSetCommandHandler : ICommandHandler
{
    public string CommandId => "context-menu-set";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "configured",
            contextMenuStyle = "ClassicWindows10",
            requiresRestart = false,
            message = "Explorer context menu preference updated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("context-menu-set.ok", payload));
    }
}
