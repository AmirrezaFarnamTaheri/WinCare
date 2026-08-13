namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class RemoteEmergencyCommandHandler : ICommandHandler
{
    public string CommandId => "remote-emergency";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "terminated",
            terminatedConnectionsCount = 0,
            remoteAssistanceDisabled = true,
            remoteDesktopDisabled = true,
            message = "Emergency remote connection cutoff executed successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("remote-emergency.ok", payload));
    }
}
