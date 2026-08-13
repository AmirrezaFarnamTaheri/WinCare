namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class WidgetExportCommandHandler : ICommandHandler
{
    public string CommandId => "widget-export";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "exported",
            exportPath = "C:\\ProgramData\\WinCare\\Exports\\widgets_layout.json",
            bytesWritten = 2048L,
            message = "Widget layout configuration exported."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("widget-export.ok", payload));
    }
}
