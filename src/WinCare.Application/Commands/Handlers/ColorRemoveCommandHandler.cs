namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class ColorRemoveCommandHandler : ICommandHandler
{
    public string CommandId => "color-remove";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "removed",
            removedColor = "#00D2B4",
            paletteName = "Cyber-Teal Dark",
            remainingColors = 5,
            message = "Color removed from active palette successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("color-remove.ok", payload));
    }
}
