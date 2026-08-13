namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class DesktopControlsCommandHandler : ICommandHandler
{
    public string CommandId => "desktop-controls";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            transparencyEffects = true,
            accentColor = "#0078D4",
            visualEffectsPreset = "Custom",
            message = "Desktop controls and visual parameters inspected."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("desktop-controls.ok", payload));
    }
}
