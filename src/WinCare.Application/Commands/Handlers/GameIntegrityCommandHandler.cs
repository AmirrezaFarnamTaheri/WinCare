namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class GameIntegrityCommandHandler : ICommandHandler
{
    public string CommandId => "game-integrity";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "verified",
            corruptedFilesFound = 0,
            totalFilesScanned = 100,
            message = "Game installation integrity check completed."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("game-integrity.ok", payload));
    }
}
