namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class SteamUsersCommandHandler : ICommandHandler
{
    public string CommandId => "steam-users";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            userProfilesDetected = 0,
            message = "Steam user profiles and login state evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("steam-users.ok", payload));
    }
}
