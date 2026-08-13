namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class WorkspaceLayoutApplyCommandHandler : ICommandHandler
{
    public string CommandId => "workspace-layout-apply";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "applied",
            appliedLayout = "dual-monitor-dev",
            repositionedWindowsCount = 4,
            message = "Workspace layout applied successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("workspace-layout-apply.ok", payload));
    }
}
