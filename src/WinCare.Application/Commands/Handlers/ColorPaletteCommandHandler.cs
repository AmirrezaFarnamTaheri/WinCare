namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class ColorPaletteCommandHandler : ICommandHandler
{
    public string CommandId => "color-palette";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "retrieved",
            activePalette = "Cyber-Teal Dark",
            colorCount = 5,
            colors = new[] { "#00D2B4", "#007A99", "#0F172A", "#1E293B", "#F8FAFC" },
            message = "Color palette retrieved successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("color-palette.ok", payload));
    }
}
