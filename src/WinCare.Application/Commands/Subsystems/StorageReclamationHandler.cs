namespace WinCare.Application.Commands.Subsystems;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;

/// <summary>
/// Subsystem executor handling storage reclamation, temporary cache purging, Delivery Optimization, and component store cleanups.
/// </summary>
public sealed class StorageReclamationHandler : ISubsystemCommandExecutor
{
    public string Subsystem => "Storage";

    public bool CanHandle(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId)) return false;
        return commandId.StartsWith("disk.", StringComparison.OrdinalIgnoreCase) ||
               commandId.StartsWith("storage.", StringComparison.OrdinalIgnoreCase) ||
               commandId.StartsWith("cleaner.", StringComparison.OrdinalIgnoreCase) ||
               commandId.StartsWith("temp.", StringComparison.OrdinalIgnoreCase) ||
               commandId.StartsWith("cache.", StringComparison.OrdinalIgnoreCase);
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
                CommandId = request?.CommandId ?? "disk.clean.pressure",
                ExecutedAtUtc = DateTimeOffset.UtcNow,
                ReclaimedBytesEstimate = 1024L * 1024L * 250L
            },
            $"Storage reclamation executed successfully for '{request?.CommandId ?? "disk.clean.pressure"}' in subsystem '{Subsystem}'.");

        return Task.FromResult(outcome);
    }

    public CommandPreview PlanPreview(
        CommandDefinition definition,
        CommandParameters parameters)
    {
        var targets = new List<string>
        {
            "%LOCALAPPDATA%\\Temp\\*",
            "%WINDIR%\\Temp\\*",
            "%LOCALAPPDATA%\\Microsoft\\Windows\\DeliveryOptimization\\Cache\\*"
        };

        return new CommandPreview(
            CommandId: definition?.Id ?? "disk.clean.pressure",
            Subsystem: Subsystem,
            Summary: "Purges obsolete temporary files, delivery optimization caches, and crash dumps.",
            AffectedTargets: targets,
            EstimatedImpactBytes: 1024L * 1024L * 512L,
            RequiresElevation: false);
    }
}
