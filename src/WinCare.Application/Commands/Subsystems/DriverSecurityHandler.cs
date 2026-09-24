namespace WinCare.Application.Commands.Subsystems;

using System;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

/// <summary>
/// Fail-closed placeholder for driver and security command routing until a concrete subsystem executor is registered.
/// </summary>
public sealed class DriverSecurityHandler : ISubsystemCommandExecutor
{
    public string Subsystem => "Security";

    public bool CanHandle(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId)) return false;
        return commandId.StartsWith("driver.", StringComparison.OrdinalIgnoreCase) ||
               commandId.StartsWith("security.", StringComparison.OrdinalIgnoreCase) ||
               commandId.StartsWith("hvci.", StringComparison.OrdinalIgnoreCase) ||
               commandId.StartsWith("vbs.", StringComparison.OrdinalIgnoreCase) ||
               commandId.StartsWith("codeintegrity.", StringComparison.OrdinalIgnoreCase);
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
            "security.executor_unavailable",
            $"No security audit is registered for '{definition.Id}'. The security posture was not verified.",
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
            Summary: "No security audit executor is registered; system security posture cannot be verified.",
            AffectedTargets: Array.Empty<string>(),
            EstimatedImpactBytes: 0,
            RequiresElevation: definition.AdministratorAccess == AdministratorAccess.Required);
    }
}
