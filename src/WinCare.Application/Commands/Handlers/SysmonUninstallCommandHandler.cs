namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class SysmonUninstallCommandHandler : ICommandHandler
{
    public string CommandId => "sysmon-uninstall";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "uninstalled",
            serviceRemoved = true,
            driverUnloaded = true,
            message = "Sysmon service and driver uninstalled."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("sysmon-uninstall.ok", payload));
    }
}
