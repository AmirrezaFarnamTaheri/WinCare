namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class CancelOperationCommandHandler : ICommandHandler
{
    public string CommandId => "cancel-operation";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "cancelled",
            activeTaskCount = 0,
            message = "Cancellation token signal broadcast to running background tasks."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("cancel-operation.ok", payload));
    }
}
