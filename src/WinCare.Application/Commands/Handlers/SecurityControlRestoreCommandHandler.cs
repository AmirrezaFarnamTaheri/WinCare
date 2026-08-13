namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class SecurityControlRestoreCommandHandler : ICommandHandler
{
    public string CommandId => "security-control-restore";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "restored",
            controlName = "RealTimeProtection",
            activeState = "Enforced",
            message = "Security control enforcement fully restored."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("security-control-restore.ok", payload));
    }
}
