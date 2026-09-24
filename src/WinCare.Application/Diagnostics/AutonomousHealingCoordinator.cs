namespace WinCare.Application.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

/// <summary>A concrete command within a diagnosed remediation plan.</summary>
public sealed record RemediationStep(string CommandId, JsonElement Parameters, string StepDescription);

/// <summary>A diagnosed finding and its proposed, ordered remediation steps.</summary>
public sealed record CorrelatedAnomaly(
    string AnomalyId,
    string PlainEnglishTitle,
    string RootCauseDescription,
    string RecommendedResolution,
    IReadOnlyList<RemediationStep> RemediationPipeline);

/// <summary>Provides findings from real diagnostic sources; this coordinator does not invent findings.</summary>
public interface IAnomalySource
{
    Task<IReadOnlyList<CorrelatedAnomaly>> ScanAsync(CancellationToken cancellationToken);
}

/// <summary>Finds eligible temporary-file cleanup targets from the core executor's preview.</summary>
public sealed class CommandBackedAnomalySource : IAnomalySource
{
    private const int CleanupAgeDays = 7;
    private readonly ICommandDispatcher _dispatcher;

    public CommandBackedAnomalySource(ICommandDispatcher dispatcher) =>
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public async Task<IReadOnlyList<CorrelatedAnomaly>> ScanAsync(CancellationToken cancellationToken)
    {
        JsonElement parameters = JsonSerializer.SerializeToElement(new { OlderThanDays = CleanupAgeDays });
        CommandResult result = await _dispatcher.ExecuteAsync(
            CommandRequest.Preview("cleaner-disk-pressure", parameters),
            CommandExecutionOptions.Default,
            cancellationToken).ConfigureAwait(false);

        if (result.Status != CommandResultStatus.Succeeded || result.Data is not JsonElement data ||
            data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("affectedResources", out JsonElement resources) ||
            resources.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Storage diagnostics could not produce a validated preview: {result.Message}");
        }

        int targetCount = 0;
        foreach (JsonElement resource in resources.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (resource.ValueKind != JsonValueKind.Object ||
                !resource.TryGetProperty("path", out JsonElement path) ||
                path.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(path.GetString()))
            {
                throw new InvalidOperationException("Storage diagnostics returned an invalid cleanup target.");
            }
            targetCount++;
            if (targetCount > 10_000)
            {
                throw new InvalidOperationException("Storage diagnostics exceeded the supported cleanup target count.");
            }
        }

        if (targetCount == 0) return Array.Empty<CorrelatedAnomaly>();

        CorrelatedAnomaly finding = new(
            "storage.cleanup.candidates",
            "Temporary files are eligible for review",
            $"A bounded cleanup preview found {targetCount} temporary-file target(s) older than {CleanupAgeDays} days.",
            "Review the exact target list and approve cleanup if it is appropriate.",
            Array.AsReadOnly(new[]
            {
                new RemediationStep(
                    "cleaner-disk-pressure",
                    parameters,
                    "Review eligible temporary files")
            }));
        return Array.AsReadOnly(new[] { finding });
    }
}

/// <summary>
/// Coordinates evidence-backed diagnostics and sends approved remediation through the command
/// dispatcher, preserving catalog validation, elevation, risk admission, and activity journaling.
/// </summary>
public sealed class AutonomousHealingCoordinator
{
    private readonly IAnomalySource _anomalySource;
    private readonly ICommandDispatcher _dispatcher;

    public AutonomousHealingCoordinator(IAnomalySource anomalySource, ICommandDispatcher dispatcher)
    {
        _anomalySource = anomalySource ?? throw new ArgumentNullException(nameof(anomalySource));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public AutonomousHealingCoordinator(ICommandDispatcher dispatcher)
        : this(new CommandBackedAnomalySource(dispatcher), dispatcher)
    {
    }

    public async Task<IReadOnlyList<CorrelatedAnomaly>> ScanForAnomaliesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CorrelatedAnomaly> anomalies = await _anomalySource.ScanAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The anomaly source returned no result collection.");

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (CorrelatedAnomaly anomaly in anomalies)
        {
            if (anomaly is null || string.IsNullOrWhiteSpace(anomaly.AnomalyId) ||
                string.IsNullOrWhiteSpace(anomaly.PlainEnglishTitle) ||
                string.IsNullOrWhiteSpace(anomaly.RootCauseDescription) ||
                string.IsNullOrWhiteSpace(anomaly.RecommendedResolution) ||
                anomaly.RemediationPipeline is null || !ids.Add(anomaly.AnomalyId))
            {
                throw new InvalidOperationException("The anomaly source returned an incomplete or duplicate finding.");
            }

            foreach (RemediationStep step in anomaly.RemediationPipeline)
            {
                if (step is null || string.IsNullOrWhiteSpace(step.CommandId) ||
                    step.Parameters.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(step.StepDescription))
                {
                    throw new InvalidOperationException($"Anomaly '{anomaly.AnomalyId}' contains an invalid remediation step.");
                }
            }
        }

        return Array.AsReadOnly(anomalies.ToArray());
    }

    /// <summary>
    /// Applies a diagnosed plan only through the command kernel. Mutating steps that require review
    /// must carry the dispatcher-issued approval associated with their command id.
    /// </summary>
    public async Task<bool> ResolveAnomalyAsync(
        CorrelatedAnomaly anomaly,
        IReadOnlyDictionary<string, ApprovedMutationPlan> approvals,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(anomaly);
        ArgumentNullException.ThrowIfNull(approvals);

        foreach (RemediationStep step in anomaly.RemediationPipeline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Reviewing: {step.StepDescription}");

            CommandDefinition? definition = WinCare.CommandCatalog.CommandCatalog.Find(step.CommandId);
            if (definition is null || definition.ReadOnly)
            {
                progress?.Report($"Step '{step.CommandId}' is not a cataloged mutation and cannot be applied as remediation.");
                return false;
            }

            ApprovedMutationPlan? approval = FindApproval(approvals, step.CommandId);
            if (approval is null)
            {
                progress?.Report($"Step '{step.CommandId}' needs a reviewed approval before it can run.");
                return false;
            }

            CommandRequest request = CommandRequest.Execute(step.CommandId, step.Parameters, approval);
            CommandResult result = await _dispatcher.ExecuteAsync(
                request,
                new CommandExecutionOptions(ReviewApproved: approval is not null),
                cancellationToken).ConfigureAwait(false);

            if (result.Status != CommandResultStatus.Succeeded)
            {
                progress?.Report($"Step '{step.CommandId}' was not completed: {result.Message}");
                return false;
            }
        }

        progress?.Report("All approved remediation steps completed.");
        return true;
    }

    private static ApprovedMutationPlan? FindApproval(
        IReadOnlyDictionary<string, ApprovedMutationPlan> approvals,
        string commandId)
    {
        foreach ((string key, ApprovedMutationPlan approval) in approvals)
        {
            if (string.Equals(key, commandId, StringComparison.OrdinalIgnoreCase)) return approval;
        }
        return null;
    }
}
