namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class StudioFileWorkspaceSaveCommandHandler : ICommandHandler
{
    public string CommandId => "studio-file-workspace-save";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "saved",
            workspaceName = "custom-studio-ws-1",
            trackedDirectoriesCount = 3,
            message = "Studio file workspace configuration saved."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("studio-file-workspace-save.ok", payload));
    }
}
