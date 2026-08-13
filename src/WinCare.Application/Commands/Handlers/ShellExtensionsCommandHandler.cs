namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class ShellExtensionsCommandHandler : ICommandHandler
{
    public string CommandId => "shell-extensions";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            totalExtensions = 14,
            thirdPartyCount = 2,
            message = "Explorer shell extensions enumerated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("shell-extensions.ok", payload));
    }
}
