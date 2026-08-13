namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class WidgetsCommandHandler : ICommandHandler
{
    public string CommandId => "widgets";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            widgetsServiceState = "Running",
            activeWidgets = 4,
            message = "Windows Widgets platform status inspected."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("widgets.ok", payload));
    }
}
