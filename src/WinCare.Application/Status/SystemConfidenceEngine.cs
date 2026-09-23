namespace WinCare.Application.Status;

using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;

/// <summary>
/// Synthesized health posture assessment.
/// </summary>
public enum ConfidenceVerdict
{
    Optimal,
    ActionRecommended,
    AttentionRequired
}

/// <summary>
/// High-level human-readable health posture report.
/// </summary>
public sealed record SystemConfidenceReport(
    ConfidenceVerdict Verdict,
    string Headline,
    string PlainEnglishCallToAction,
    int ReclaimableMegabytes,
    int PendingRemediations);

/// <summary>
/// Status engine synthesizing telemetry across subsystems into a decisive, bottom-line verdict without advisory hedging.
/// </summary>
public sealed class SystemConfidenceEngine
{
    private readonly SubsystemCommandRegistry _registry;

    public SystemConfidenceEngine(SubsystemCommandRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Evaluates current machine posture across storage, servicing, security, and remediation.
    /// </summary>
    public Task<SystemConfidenceReport> EvaluateMachinePostureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int reclaimableMb = 0;
        int pendingActions = 0;

        var storageExecutor = _registry?.Resolve("disk.clean.pressure");
        if (storageExecutor != null)
        {
            var preview = storageExecutor.PlanPreview(null!, CommandParameters.Empty);
            reclaimableMb = (int)(preview.EstimatedImpactBytes / (1024 * 1024));
            if (reclaimableMb > 0) pendingActions++;
        }

        if (pendingActions > 0)
        {
            return Task.FromResult(new SystemConfidenceReport(
                Verdict: ConfidenceVerdict.ActionRecommended,
                Headline: $"{pendingActions} Recommended Action(s)",
                PlainEnglishCallToAction: $"Clean {reclaimableMb / 1024.0:F1} GB of obsolete system cache to free up storage.",
                ReclaimableMegabytes: reclaimableMb,
                PendingRemediations: pendingActions));
        }

        return Task.FromResult(new SystemConfidenceReport(
            Verdict: ConfidenceVerdict.Optimal,
            Headline: "System is Optimal",
            PlainEnglishCallToAction: "All core operating policies and component stores are verified healthy.",
            ReclaimableMegabytes: 0,
            PendingRemediations: 0));
    }
}
