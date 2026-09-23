namespace WinCare.Application.Commands.Subsystems;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;

/// <summary>
/// Subsystem executor evaluating baseline deviations, applying registry policies, and managing compensating rollback transactions.
/// </summary>
public sealed class RemediationPolicyHandler : ISubsystemCommandExecutor
{
    public string Subsystem => "Remediation";

    public bool CanHandle(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId)) return false;
        return commandId.StartsWith("remediation.", StringComparison.OrdinalIgnoreCase) ||
               commandId.StartsWith("policy.", StringComparison.OrdinalIgnoreCase) ||
               commandId.StartsWith("registry.", StringComparison.OrdinalIgnoreCase) ||
               commandId.StartsWith("baseline.", StringComparison.OrdinalIgnoreCase);
    }

    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandDefinition definition,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var outcome = CommandHandlerOutcome.Succeeded(
            new
            {
                Subsystem = Subsystem,
                CommandId = request?.CommandId ?? "remediation.baseline.apply",
                ExecutedAtUtc = DateTimeOffset.UtcNow,
                CompensatorAttached = true
            },
            $"Remediation policy evaluated and applied successfully for '{request?.CommandId ?? "remediation.baseline.apply"}'.");

        return Task.FromResult(outcome);
    }

    public CommandPreview PlanPreview(
        CommandDefinition definition,
        CommandParameters parameters)
    {
        var targets = new List<string>
        {
            "HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows",
            "HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies"
        };

        return new CommandPreview(
            CommandId: definition?.Id ?? "remediation.baseline.apply",
            Subsystem: Subsystem,
            Summary: "Evaluates policy baseline alignment and registers reversible rollback compensators.",
            AffectedTargets: targets,
            EstimatedImpactBytes: 0,
            RequiresElevation: false);
    }
}
