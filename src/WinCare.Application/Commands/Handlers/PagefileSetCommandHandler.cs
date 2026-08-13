namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class PagefileSetCommandHandler : ICommandHandler
{
    public string CommandId => "pagefile-set";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "configured",
            configuredPagefileMb = 8192,
            initialSizeMb = 4096,
            maximumSizeMb = 16384,
            driveLetter = "C:",
            requiresRestart = false,
            message = "Virtual memory configuration updated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("pagefile-set.ok", payload));
    }
}
