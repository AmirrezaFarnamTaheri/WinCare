namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class PowerStopCommandHandler : ICommandHandler
{
    public string CommandId => "power-stop";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "stopped",
            sessionType = "ExecutionLock",
            message = "Power request override lock released."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("power-stop.ok", payload));
    }
}
