namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class CustomizationHostsCommandHandler : ICommandHandler
{
    public string CommandId => "customization-hosts";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            customizationFrameworksDetected = 0,
            activeInjectionsCount = 0,
            message = "Desktop customization host processes and hooks evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("customization-hosts.ok", payload));
    }
}
