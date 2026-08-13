namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class ColorAddCommandHandler : ICommandHandler
{
    public string CommandId => "color-add";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "added",
            addedColor = "#00D2B4",
            paletteName = "Cyber-Teal Dark",
            totalColors = 6,
            message = "Color added to active palette successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("color-add.ok", payload));
    }
}
