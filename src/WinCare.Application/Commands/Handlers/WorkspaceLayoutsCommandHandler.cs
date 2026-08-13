namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class WorkspaceLayoutsCommandHandler : ICommandHandler
{
    public string CommandId => "workspace-layouts";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "retrieved",
            activeLayout = "dual-monitor-dev",
            savedLayouts = new[] { "default", "dual-monitor-dev", "focus-mode", "gaming" },
            message = "Workspace window layouts retrieved successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("workspace-layouts.ok", payload));
    }
}
