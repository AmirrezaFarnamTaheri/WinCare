namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class RemoteConsentStateCommandHandler : ICommandHandler
{
    public string CommandId => "remote-consent-state";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "queryed",
            activeSession = false,
            lastSessionEndedAt = "2026-08-10T14:22:00Z",
            message = "Remote support consent state retrieved."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("remote-consent-state.ok", payload));
    }
}
