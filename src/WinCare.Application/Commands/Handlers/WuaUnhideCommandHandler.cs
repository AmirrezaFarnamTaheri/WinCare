namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class WuaUnhideCommandHandler : ICommandHandler
{
    public string CommandId => "wua-unhide";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "unhidden",
            unhiddenUpdatesCount = 1,
            message = "Previously hidden Windows updates unhidden for detection."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("wua-unhide.ok", payload));
    }
}
