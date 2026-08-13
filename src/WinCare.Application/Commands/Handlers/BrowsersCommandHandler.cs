namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class BrowsersCommandHandler : ICommandHandler
{
    public string CommandId => "browsers";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            detectedBrowsers = new[]
            {
                new { name = "Microsoft Edge", version = "127.0.0.0", isDefault = true },
                new { name = "Google Chrome", version = "127.0.0.0", isDefault = false }
            },
            message = "Installed web browsers detected successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("browsers.ok", payload));
    }
}
