namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class RemoteConsentCreateCommandHandler : ICommandHandler
{
    public string CommandId => "remote-consent-create";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "created",
            sessionId = "rc-sess-8812",
            expiresInMinutes = 30,
            message = "One-time remote support consent session created."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("remote-consent-create.ok", payload));
    }
}
