namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class WidgetCatalogCommandHandler : ICommandHandler
{
    public string CommandId => "widget-catalog";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "retrieved",
            registeredProvidersCount = 6,
            message = "Widget provider catalog retrieved."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("widget-catalog.ok", payload));
    }
}
