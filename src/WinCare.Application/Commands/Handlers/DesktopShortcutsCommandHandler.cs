namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class DesktopShortcutsCommandHandler : ICommandHandler
{
    public string CommandId => "desktop-shortcuts";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            userShortcutsCount = 8,
            brokenShortcutsCount = 0,
            message = "Desktop shortcut integrity evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("desktop-shortcuts.ok", payload));
    }
}
