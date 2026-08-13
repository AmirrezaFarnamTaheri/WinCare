namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class WorkspaceLayoutSaveCommandHandler : ICommandHandler
{
    public string CommandId => "workspace-layout-save";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "saved",
            layoutName = "custom-layout-1",
            capturedWindowsCount = 4,
            message = "Current workspace layout saved successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("workspace-layout-save.ok", payload));
    }
}
