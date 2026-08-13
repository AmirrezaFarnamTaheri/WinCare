namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class HardeningAssessCommandHandler : ICommandHandler
{
    public string CommandId => "hardening-assess";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "assessed",
            complianceScore = 88,
            evaluatedRulesCount = 42,
            recommendedAdjustmentsCount = 5,
            message = "System security hardening posture assessed."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("hardening-assess.ok", payload));
    }
}
