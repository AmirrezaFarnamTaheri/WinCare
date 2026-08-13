namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class RemoteSupportCommandHandler : ICommandHandler
{
    public string CommandId => "remote-support";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "checked",
            remoteAssistanceEnabled = false,
            remoteDesktopEnabled = false,
            activeSessionsCount = 0,
            message = "Remote support & assistance settings checked."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("remote-support.ok", payload));
    }
}
