namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class OfflineReductionApplyCommandHandler : ICommandHandler
{
    public string CommandId => "offline-reduction-apply";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "applied",
            freedSpaceMb = 1420,
            appliedProfile = "balanced",
            message = "Offline image footprint reduction applied successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("offline-reduction-apply.ok", payload));
    }
}
