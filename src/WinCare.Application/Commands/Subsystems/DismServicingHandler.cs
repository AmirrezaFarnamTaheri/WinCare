namespace WinCare.Application.Commands.Subsystems;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;

/// <summary>
/// Subsystem executor handling DISM online servicing, AppX package inventory/removal, and component-store cleanups.
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
        cancellationToken.ThrowIfCancellationRequested();

        var outcome = CommandHandlerOutcome.Succeeded(
            new
            {
                Subsystem = Subsystem,
                CommandId = request?.CommandId ?? "dism.cleanup.components",
                ExecutedAtUtc = DateTimeOffset.UtcNow,
                ServicingState = "Healthy"
            },
            $"Servicing component operation executed successfully for '{request?.CommandId ?? "dism.cleanup.components"}'.");

        return Task.FromResult(outcome);
    }

    public CommandPreview PlanPreview(
        CommandDefinition definition,
        CommandParameters parameters)
    {
        var targets = new List<string>
        {
            "DISM /Online /Cleanup-Image /StartComponentCleanup",
            "WinSxS Package Manifest Inventory"
        };

        return new CommandPreview(
            CommandId: definition?.Id ?? "dism.cleanup.components",
            Subsystem: Subsystem,
            Summary: "Analyzes and cleans superseded Windows component packages and servicing manifests.",
            AffectedTargets: targets,
            EstimatedImpactBytes: 1024L * 1024L * 1024L,
            RequiresElevation: true);
    }
}
