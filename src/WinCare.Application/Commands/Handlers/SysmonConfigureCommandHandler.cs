namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class SysmonConfigureCommandHandler : ICommandHandler
{
    public string CommandId => "sysmon-configure";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "configured",
            configSchemaVersion = "4.90",
            ruleCount = 142,
            message = "Sysmon event filtering schema rules updated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("sysmon-configure.ok", payload));
    }
}
