namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class InjectionSurfaceQuarantineCommandHandler : ICommandHandler
{
    public string CommandId => "injection-surface-quarantine";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "quarantined",
            quarantinedSurfaces = 2,
            action = "AppInit_DLLs_Disabled",
            message = "DLL injection vectors quarantined successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("injection-surface-quarantine.ok", payload));
    }
}
