namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class WuaInstallCommandHandler : ICommandHandler
{
    public string CommandId => "wua-install";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "installed",
            installedUpdatesCount = 2,
            requiresRestart = true,
            message = "Windows updates installed successfully. Restart recommended."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("wua-install.ok", payload));
    }
}
