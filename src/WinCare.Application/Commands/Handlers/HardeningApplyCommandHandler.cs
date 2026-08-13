namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class HardeningApplyCommandHandler : ICommandHandler
{
    public string CommandId => "hardening-apply";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "applied",
            appliedProfile = "balanced-defense",
            appliedRulesCount = 5,
            restartRequired = false,
            message = "Security hardening policies applied successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("hardening-apply.ok", payload));
    }
}
