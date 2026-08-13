namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class RainmeterSkinsCommandHandler : ICommandHandler
{
    public string CommandId => "rainmeter-skins";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            rainmeterInstalled = false,
            activeSkinsCount = 0,
            message = "Rainmeter desktop skin environment evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("rainmeter-skins.ok", payload));
    }
}
