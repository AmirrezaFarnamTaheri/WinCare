namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class HardeningProfilesCommandHandler : ICommandHandler
{
    public string CommandId => "hardening-profiles";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "retrieved",
            activeProfile = "balanced-defense",
            profiles = new[] { "baseline", "balanced-defense", "strict-isolation", "airgapped" },
            message = "System security hardening profiles enumerated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("hardening-profiles.ok", payload));
    }
}
