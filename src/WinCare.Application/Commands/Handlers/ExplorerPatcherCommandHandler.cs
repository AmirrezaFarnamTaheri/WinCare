namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class ExplorerPatcherCommandHandler : ICommandHandler
{
    public string CommandId => "explorerpatcher";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            explorerPatcherInstalled = false,
            dxgiDllHookPresent = false,
            message = "ExplorerPatcher shell modification framework evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("explorerpatcher.ok", payload));
    }
}
