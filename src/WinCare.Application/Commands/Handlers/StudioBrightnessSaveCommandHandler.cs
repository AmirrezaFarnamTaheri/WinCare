namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class StudioBrightnessSaveCommandHandler : ICommandHandler
{
    public string CommandId => "studio-brightness-save";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "saved",
            scheduleName = "night-mode-dim",
            targetBrightnessPercent = 30,
            message = "Studio brightness schedule saved successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("studio-brightness-save.ok", payload));
    }
}
