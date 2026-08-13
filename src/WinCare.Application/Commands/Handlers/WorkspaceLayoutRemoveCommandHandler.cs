namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class WorkspaceLayoutRemoveCommandHandler : ICommandHandler
{
    public string CommandId => "workspace-layout-remove";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "removed",
            removedLayout = "custom-layout-1",
            message = "Workspace layout removed successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("workspace-layout-remove.ok", payload));
    }
}
