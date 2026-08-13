namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class SysmonCommandHandler : ICommandHandler
{
    public string CommandId => "sysmon";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            serviceState = "NotInstalled",
            driverLoaded = false,
            message = "Sysinternals Sysmon telemetry agent status evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("sysmon.ok", payload));
    }
}
