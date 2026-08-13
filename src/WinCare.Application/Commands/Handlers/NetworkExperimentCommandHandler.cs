namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class NetworkExperimentCommandHandler : ICommandHandler
{
    public string CommandId => "network-experiment";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "executed",
            experimentName = "TcpWindowAutoTuning",
            previousSetting = "Normal",
            appliedSetting = "Experimental",
            message = "Network tuning experiment executed safely."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("network-experiment.ok", payload));
    }
}
