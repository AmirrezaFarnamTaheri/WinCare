using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Tools;
using WinCare.CommandCatalog.Models;

namespace WinCare.Application.Diagnostics
{
    public interface IIntentTranslator
    {
        Task<DoctorActionPlan> TranslateAsync(string prompt, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Translates a classified intent into a fail-closed <see cref="DoctorActionPlan"/> whose
    /// proposed steps are drawn strictly from the native catalog, prioritizing read-only
    /// inspection commands before any mutating recommendation.
    /// </summary>
    public sealed class IntentTranslator : IIntentTranslator
    {
        private readonly IIntentInferenceEngine _inferenceEngine;
        private readonly ToolCatalogService _catalogService;
        private readonly IDiagnosticEvidenceCollector _evidenceCollector;

        public IntentTranslator(
            IIntentInferenceEngine inferenceEngine,
            ToolCatalogService catalogService,
            IDiagnosticEvidenceCollector? evidenceCollector = null)
        {
            _inferenceEngine = inferenceEngine ?? throw new ArgumentNullException(nameof(inferenceEngine));
            _catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
            _evidenceCollector = evidenceCollector ?? new DiagnosticEvidenceCollector();
        }

        public async Task<DoctorActionPlan> TranslateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("Prompt cannot be empty", nameof(prompt));
            }

            var intent = await _inferenceEngine.PredictIntentAsync(prompt, cancellationToken);
            var evidence = await _evidenceCollector.CollectEvidenceAsync(intent, cancellationToken);
            var findings = new List<DiagnosticFinding>();
            var proposedSteps = new List<ProposedActionStep>();
            DiagnosticSeverity severity = DiagnosticSeverity.Information;
            string summary;

            var pressureEvidence = evidence.FirstOrDefault(e => e.IndicatesPressure);
            bool hasTelemetryEvidence = pressureEvidence != null;

            switch (intent)
            {
                case "intent.storage.cleanup":
                    severity = hasTelemetryEvidence ? DiagnosticSeverity.Warning : DiagnosticSeverity.Information;
                    summary = hasTelemetryEvidence
                        ? $"Storage pressure verified by telemetry: {pressureEvidence!.MetricName} ({pressureEvidence.MeasuredValue}). Cleanup is recommended."
                        : "Inferred area of interest: Storage optimization. Diagnostic probe gathered drive metrics. Run inspection to measure cache sizes.";
                    findings.Add(new DiagnosticFinding(
                        "finding.storage.temp",
                        hasTelemetryEvidence ? "Measured Observation: Storage Pressure on System Drive" : "Investigative Hypothesis: Temporary File Accumulation",
                        hasTelemetryEvidence ? $"Live telemetry confirms {pressureEvidence!.MeasuredValue}.\n[Provenance: {pressureEvidence.ProvenanceSummary}]" : "Natural language matched storage cleanup inquiry. Telemetry inspection is recommended to quantify reclaimable disk space.",
                        severity,
                        "Storage (C:)",
                        IsVerifiedByTelemetry: hasTelemetryEvidence
                    ));

                    // Read-only inspection first, mutating recommendation last (with concrete parameters).
                    AddRecommendedSteps(proposedSteps,
                        "cleaner-preview-cards",
                        "cleanup-targets",
                        "storage",
                        "cleaner-disk-pressure");
                    break;

                case "intent.memory.optimize":
                    severity = hasTelemetryEvidence ? DiagnosticSeverity.Warning : DiagnosticSeverity.Information;
                    summary = hasTelemetryEvidence
                        ? $"Memory pressure verified by telemetry: {pressureEvidence!.MetricName} ({pressureEvidence.MeasuredValue}). Optimization is recommended."
                        : "Inferred area of interest: Memory working sets. Diagnostic probe gathered current memory load. Run inspection before trimming caches.";
                    findings.Add(new DiagnosticFinding(
                        "finding.memory.standby",
                        hasTelemetryEvidence ? "Measured Observation: High Memory Utilization" : "Investigative Hypothesis: Working Set & Standby List Pressure",
                        hasTelemetryEvidence ? $"Live telemetry confirms {pressureEvidence!.MeasuredValue}.\n[Provenance: {pressureEvidence.ProvenanceSummary}]" : "Natural language matched memory responsiveness inquiry. Run memory diagnostics to measure commit charge and working set sizes.",
                        severity,
                        "System RAM",
                        IsVerifiedByTelemetry: hasTelemetryEvidence
                    ));

                    AddRecommendedSteps(proposedSteps, "internals-memory", "health", "system");
                    break;

                case "intent.network.flush":
                    severity = DiagnosticSeverity.Information;
                    summary = "Inferred area of interest: Network & DNS resolution. Run network diagnostic probes before resetting adapter caches.";
                    findings.Add(new DiagnosticFinding(
                        "finding.network.dns",
                        "Possible Cause: Stale DNS Cache or Socket Latency (Pending Verification)",
                        "Natural language matched network connectivity inquiry. Run diagnostic queries to test DNS resolution latency.",
                        DiagnosticSeverity.Information,
                        "Network Adapter",
                        IsVerifiedByTelemetry: false
                    ));

                    AddRecommendedSteps(proposedSteps, "network", "network-measure", "tcp-global");
                    break;

                case "intent.privacy.harden":
                    severity = DiagnosticSeverity.Information;
                    summary = "Inferred area of interest: Diagnostic telemetry & privacy preferences. Review configured policy state before modifying settings.";
                    findings.Add(new DiagnosticFinding(
                        "finding.privacy.telemetry",
                        "Possible Cause: Diagnostic Data Collection Policies (Pending Verification)",
                        "Natural language matched privacy settings inquiry. Inspect current Windows diagnostic settings prior to applying overrides.",
                        DiagnosticSeverity.Information,
                        "Privacy Settings",
                        IsVerifiedByTelemetry: false
                    ));

                    AddRecommendedSteps(proposedSteps,
                        "security-controls",
                        "telemetry-snapshot",
                        "experience-privacy-profiles",
                        "experience-privacy-apply");
                    break;

                case "intent.apps.update":
                    severity = DiagnosticSeverity.Information;
                    summary = "Inferred area of interest: Package updates. Query package manager manifests to verify pending updates.";
                    findings.Add(new DiagnosticFinding(
                        "finding.apps.outdated",
                        "Possible Cause: Available Application Updates (Pending Verification)",
                        "Natural language matched application update inquiry. Query WinGet repository sources to verify package update availability.",
                        DiagnosticSeverity.Information,
                        "WinGet Package Manager",
                        IsVerifiedByTelemetry: false
                    ));

                    AddRecommendedSteps(proposedSteps, "wua-search", "wua-history");
                    break;

                default:
                    severity = DiagnosticSeverity.Information;
                    summary = "Inferred area of interest: General system inquiry. Recommended diagnostic inspection checks are available.";
                    findings.Add(new DiagnosticFinding(
                        "finding.general.inquiry",
                        "General system check requested",
                        "The description alone cannot establish system health. Collect diagnostic evidence before deciding on repairs.",
                        DiagnosticSeverity.Information,
                        "System",
                        IsVerifiedByTelemetry: false
                    ));
                    AddRecommendedSteps(proposedSteps, "system", "storage", "security");
                    break;
            }

            // Fallback: If no steps were mapped, provide the safest general read-only command.
            if (proposedSteps.Count == 0 && severity != DiagnosticSeverity.Healthy)
            {
                var fallback = _catalogService.All.FirstOrDefault(c => c.ReadOnly &&
                    c.MigrationStatus is MigrationStatus.Implemented or MigrationStatus.BehaviorVerified);
                if (fallback != null)
                {
                    proposedSteps.Add(CreateStep(fallback));
                }
            }

            return new DoctorActionPlan
            {
                PlanId = "plan_" + Guid.NewGuid().ToString("N")[..8],
                NaturalLanguageQuery = prompt,
                DiagnosisSummary = summary,
                OverallSeverity = severity,
                Findings = findings,
                ProposedSteps = proposedSteps,
                MeasuredEvidence = evidence
            };
        }

        /// <summary>
        /// Adds catalog commands (by exact ID) as proposed steps, skipping duplicates and capping
        /// at five recommendations. Read-only IDs should be listed before any mutating ID.
        /// </summary>
        private void AddRecommendedSteps(List<ProposedActionStep> steps, params string[] commandIds)
        {
            foreach (var id in commandIds)
            {
                var match = _catalogService.All.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (match == null || match.MigrationStatus is not (MigrationStatus.Implemented or MigrationStatus.BehaviorVerified))
                {
                    continue;
                }
                if (steps.Any(s => s.CommandId.Equals(match.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                steps.Add(CreateStep(match));
                if (steps.Count >= 5)
                {
                    return;
                }
            }
        }

        private static ProposedActionStep CreateStep(CommandDefinition match) =>
            new(
                CommandId: match.Id,
                Title: match.Title,
                Description: match.Summary,
                RiskLevel: match.Risk,
                RequiresElevation: match.AdministratorAccess == AdministratorAccess.Required,
                Parameters: DefaultParameters(match),
                AffectedResource: match.Area,
                UndoAvailable: match.Risk != CommandRisk.Critical
            );

        /// <summary>
        /// Supplies safe, concrete parameters for the parameterized commands the Doctor may
        /// recommend, so recommended mutating steps do not silently fail parameter validation.
        /// </summary>
        private static IReadOnlyDictionary<string, string>? DefaultParameters(CommandDefinition command) =>
            command.Id switch
            {
                // Purge only files older than 7 days; a conservative, reviewable default.
                "cleaner-disk-pressure" => new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["OlderThanDays"] = "7"
                },
                // Apply the maximum-privacy profile by disabling telemetry; reviewable before execution.
                "experience-privacy-apply" => new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["IncludeTelemetry"] = "false"
                },
                _ => null
            };
    }
}
