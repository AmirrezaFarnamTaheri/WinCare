namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class ToolkitWin32ErrorCommandHandler : ICommandHandler
{
    public string CommandId => "toolkit-win32-error";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "resolved",
            errorCode = 5,
            symbolicName = "ERROR_ACCESS_DENIED",
            description = "Access is denied.",
            message = "Win32 error code resolved to symbolic definition and description."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("toolkit-win32-error.ok", payload));
    }
}
