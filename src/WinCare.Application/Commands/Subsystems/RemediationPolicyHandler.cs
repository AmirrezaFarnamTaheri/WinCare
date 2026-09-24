namespace WinCare.Application.Commands.Subsystems;

using System;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

/// <summary>
/// Fail-closed placeholder for remediation command routing until a concrete subsystem executor is registered.
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
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(definition.Id, request.CommandId, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(CommandHandlerOutcome.Blocked("command.definition_mismatch", "The command definition does not match the request."));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CommandHandlerOutcome(
            CommandResultStatus.NotMigrated,
            "remediation.executor_unavailable",
            $"No remediation operation is registered for '{definition.Id}'. No policy was changed.",
            null,
            false));
    }

    public CommandPreview PlanPreview(
        CommandDefinition definition,
        SubsystemCommandParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(parameters);

        return new CommandPreview(
            CommandId: definition.Id,
            Subsystem: Subsystem,
            Summary: "No remediation executor is registered; no changes or rollback plan can be previewed.",
            AffectedTargets: Array.Empty<string>(),
            EstimatedImpactBytes: 0,
            RequiresElevation: definition.AdministratorAccess == AdministratorAccess.Required);
    }
}
