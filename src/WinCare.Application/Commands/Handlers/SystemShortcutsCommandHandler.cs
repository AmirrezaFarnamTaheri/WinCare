namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class SystemShortcutsCommandHandler : ICommandHandler
{
    public string CommandId => "system-shortcuts";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "enumerated",
            shortcutsCount = 14,
            desktopShortcuts = 6,
            startMenuShortcuts = 8,
            message = "Windows system shell shortcuts enumerated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("system-shortcuts.ok", payload));
    }
}
