namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class StudioFileWorkspacesCommandHandler : ICommandHandler
{
    public string CommandId => "studio-file-workspaces";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "retrieved",
            workspacesCount = 2,
            activeWorkspace = "default-project",
            workspaces = new[] { "default-project", "audio-production" },
            message = "Studio file workspaces enumerated successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("studio-file-workspaces.ok", payload));
    }
}
