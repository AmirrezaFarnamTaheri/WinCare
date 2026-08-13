namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class MonitorsCommandHandler : ICommandHandler
{
    public string CommandId => "monitors";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            monitorsCount = 1,
            primaryResolution = "1920x1080",
            dpiScale = "100%",
            message = "Display monitor layout and topology evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("monitors.ok", payload));
    }
}
