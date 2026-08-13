namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class SecurityControlReduceCommandHandler : ICommandHandler
{
    public string CommandId => "security-control-reduce";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "reduced",
            controlName = "RealTimeProtection",
            action = "TemporarilyReduced",
            durationMinutes = 15,
            message = "Security control temporarily reduced under approved review policy."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("security-control-reduce.ok", payload));
    }
}
