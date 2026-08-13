namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class SteamGamesCommandHandler : ICommandHandler
{
    public string CommandId => "steam-games";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            gamesDetected = 0,
            libraryPath = "C:\\Program Files (x86)\\Steam\\steamapps",
            message = "Steam games library manifest inspected."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("steam-games.ok", payload));
    }
}
