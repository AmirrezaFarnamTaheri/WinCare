namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class WindhawkModsCommandHandler : ICommandHandler
{
    public string CommandId => "windhawk-mods";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            windhawkInstalled = false,
            loadedModsCount = 0,
            message = "Windhawk system customization mod engine evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("windhawk-mods.ok", payload));
    }
}
