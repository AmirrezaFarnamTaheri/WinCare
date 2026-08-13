namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class StudioBrightnessSchedulesCommandHandler : ICommandHandler
{
    public string CommandId => "studio-brightness-schedules";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "retrieved",
            schedulesCount = 2,
            activeSchedule = "daylight-adaptive",
            schedules = new[]
            {
                new { name = "daylight-adaptive", enabled = true },
                new { name = "night-mode-dim", enabled = false }
            },
            message = "Studio display brightness schedules retrieved."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("studio-brightness-schedules.ok", payload));
    }
}
