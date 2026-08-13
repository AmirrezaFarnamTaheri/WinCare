namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class StudioLayoutProfilesCommandHandler : ICommandHandler
{
    public string CommandId => "studio-layout-profiles";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "retrieved",
            activeProfile = "broadcast-standard",
            profiles = new[] { "broadcast-standard", "editing-dual", "mastering-wide" },
            message = "Studio layout profiles retrieved."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("studio-layout-profiles.ok", payload));
    }
}
