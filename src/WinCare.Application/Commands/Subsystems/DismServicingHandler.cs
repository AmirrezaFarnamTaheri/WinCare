namespace WinCare.Application.Commands.Subsystems;

using System;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

/// <summary>
/// Fail-closed placeholder for servicing command routing until a concrete subsystem executor is registered.
/// </summary>
public sealed class DismServicingHandler : ISubsystemCommandExecutor
{
    public string Subsystem => "Servicing";

    public bool CanHandle(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId)) return false;
        return commandId.StartsWith("dism.", StringComparison.OrdinalIgnoreCase) ||
               commandId.StartsWith("servicing.", StringComparison.OrdinalIgnoreCase) ||
               commandId.StartsWith("appx.", StringComparison.OrdinalIgnoreCase) ||
               commandId.StartsWith("component.", StringComparison.OrdinalIgnoreCase) ||
               commandId.StartsWith("winsxs.", StringComparison.OrdinalIgnoreCase);
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
            "servicing.executor_unavailable",
            $"No servicing operation is registered for '{definition.Id}'. No Windows components were changed.",
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
            Summary: "No servicing executor is registered; no operation or impact estimate is available.",
            AffectedTargets: Array.Empty<string>(),
            EstimatedImpactBytes: 0,
            RequiresElevation: definition.AdministratorAccess == AdministratorAccess.Required);
    }
}
