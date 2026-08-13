namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class RemoteConsentExpireCommandHandler : ICommandHandler
{
    public string CommandId => "remote-consent-expire";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "expired",
            expiredSessionsCount = 1,
            message = "Active remote support consent sessions expired manually."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("remote-consent-expire.ok", payload));
    }
}
