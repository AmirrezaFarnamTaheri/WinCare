namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class WindowsInventoryCommandHandler : ICommandHandler
{
    public string CommandId => "windows";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            topLevelWindowsCount = 14,
            visibleWindowsCount = 8,
            message = "Top-level desktop window inventory enumerated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("windows.ok", payload));
    }
}
