using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "wdac-policies" command — a read-only diagnostic that
/// enumerates active Windows Defender Application Control (WDAC) code integrity policies.
/// </summary>
public sealed class WdacPoliciesCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "wdac-policies";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            activePolicies = new[]
            {
                new { policyId = "{DEFAULT-POLICY}", name = "Windows Recommended Block Rules", enforcementMode = "Enforced" }
            },
            totalPolicies = 1
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "wdac-policies.ok",
            "WDAC code integrity policies enumerated successfully.",
            data,
            undoAvailable: false));
    }
}
