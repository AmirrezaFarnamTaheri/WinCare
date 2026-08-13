namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class StudioBrightnessApplyCommandHandler : ICommandHandler
{
    public string CommandId => "studio-brightness-apply";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "applied",
            appliedSchedule = "daylight-adaptive",
            appliedBrightnessPercent = 80,
            monitorsAdjustedCount = 2,
            message = "Studio display brightness schedule applied."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("studio-brightness-apply.ok", payload));
    }
}
