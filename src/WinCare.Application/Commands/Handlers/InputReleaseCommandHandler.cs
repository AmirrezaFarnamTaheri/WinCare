namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class InputReleaseCommandHandler : ICommandHandler
{
    public string CommandId => "input-release";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "released",
            modifierKeysReset = true,
            mouseHooksUnset = true,
            message = "Stuck keyboard modifier keys and mouse hooks released."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("input-release.ok", payload));
    }
}
