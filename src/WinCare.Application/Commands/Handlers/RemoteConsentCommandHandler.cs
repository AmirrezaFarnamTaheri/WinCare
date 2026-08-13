namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class RemoteConsentCommandHandler : ICommandHandler
{
    public string CommandId => "remote-consent";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "evaluated",
            consentRequired = true,
            promptLevel = "Strict",
            activeConsentsCount = 0,
            message = "Remote assistance consent policy evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("remote-consent.ok", payload));
    }
}
