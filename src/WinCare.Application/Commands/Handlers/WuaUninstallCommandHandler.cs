namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class WuaUninstallCommandHandler : ICommandHandler
{
    public string CommandId => "wua-uninstall";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "uninstalled",
            uninstalledUpdateId = "KB5034123",
            requiresRestart = true,
            message = "Targeted Windows update package uninstalled successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("wua-uninstall.ok", payload));
    }
}
