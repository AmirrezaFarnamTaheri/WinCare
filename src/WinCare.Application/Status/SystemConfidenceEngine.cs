namespace WinCare.Application.Status;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;

/// <summary>Evidence-based posture verdict.</summary>
public enum ConfidenceVerdict { Unavailable, Optimal, ActionRecommended, AttentionRequired }

/// <summary>Storage assessment result; it does not imply health of unassessed subsystems.</summary>
public sealed record SystemConfidenceReport(
    ConfidenceVerdict Verdict,
    string Headline,
    string PlainEnglishCallToAction,
    int ReclaimableMegabytes,
    int PendingRemediations,
    string AssessedArea,
    int AffectedTargetCount);

/// <summary>Derives a storage posture from the command kernel's validated, read-only preview.</summary>
public sealed class SystemConfidenceEngine
{
    private readonly ICommandDispatcher _dispatcher;

    public SystemConfidenceEngine(ICommandDispatcher dispatcher) =>
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public async Task<SystemConfidenceReport> EvaluateMachinePostureAsync(CancellationToken cancellationToken)
    {
        CommandResult result = await _dispatcher.ExecuteAsync(
            CommandRequest.Preview("cleaner-disk-pressure", JsonSerializer.SerializeToElement(new { OlderThanDays = 7 })),
            CommandExecutionOptions.Default,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (result.Status != CommandResultStatus.Succeeded || result.Data is not JsonElement data ||
            data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("affectedResources", out JsonElement resources) || resources.ValueKind != JsonValueKind.Array)
        {
            return new SystemConfidenceReport(
                ConfidenceVerdict.Unavailable,
                "Storage posture unavailable",
                "WinCare could not obtain a validated cleanup preview. No system health conclusion was made.",
                0,
                0,
                "Storage",
                0);
        }

        HashSet<string> targets = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement resource in resources.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (resource.ValueKind != JsonValueKind.Object ||
                !resource.TryGetProperty("path", out JsonElement path) ||
                path.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(path.GetString()))
            {
                return new SystemConfidenceReport(
                    ConfidenceVerdict.Unavailable,
                    "Storage posture unavailable",
                    "The cleanup preview included an invalid target; no system health conclusion was made.",
                    0,
                    0,
                    "Storage",
                    0);
            }

            targets.Add(path.GetString()!);
            if (targets.Count > 10_000)
            {
                return new SystemConfidenceReport(
                    ConfidenceVerdict.Unavailable,
                    "Storage posture unavailable",
                    "The cleanup preview exceeded the supported target count; no system health conclusion was made.",
                    0,
                    0,
                    "Storage",
                    0);
            }
        }

        int targetCount = targets.Count;
        if (targetCount > 0)
        {
            return new SystemConfidenceReport(
                ConfidenceVerdict.ActionRecommended,
                "Cleanup candidates found",
                $"The preview identified {targetCount} cleanup target(s). Review the command preview before applying any changes.",
                0,
                1,
                "Storage",
                targetCount);
        }

        return new SystemConfidenceReport(
            ConfidenceVerdict.Optimal,
            "Storage looks healthy",
            "The cleanup preview found no eligible targets. Other system areas were not assessed.",
            0,
            0,
            "Storage",
            0);
    }
}
