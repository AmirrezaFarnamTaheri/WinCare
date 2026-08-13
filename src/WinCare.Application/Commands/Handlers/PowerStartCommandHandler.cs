namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class PowerStartCommandHandler : ICommandHandler
{
    public string CommandId => "power-start";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "started",
            sessionType = "ExecutionLock",
            lockReason = "UserTaskInFlight",
            message = "Power request override lock activated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("power-start.ok", payload));
    }
}
