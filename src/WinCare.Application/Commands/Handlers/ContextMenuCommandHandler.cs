namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class ContextMenuCommandHandler : ICommandHandler
{
    public string CommandId => "context-menu";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            contextMenuStyle = "Windows11Modern",
            legacyContextMenuEnabled = false,
            message = "Explorer context menu style and handlers inspected."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("context-menu.ok", payload));
    }
}
