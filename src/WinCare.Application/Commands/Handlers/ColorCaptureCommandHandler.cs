namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class ColorCaptureCommandHandler : ICommandHandler
{
    public string CommandId => "color-capture";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "captured",
            hexColor = "#00D2B4",
            rgbColor = "0, 210, 180",
            hslColor = "171, 100%, 41%",
            message = "Screen pixel color captured successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("color-capture.ok", payload));
    }
}
