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
            _inferenceEngine = inferenceEngine;
            _catalogService = catalogService;
            _evidenceCollector = evidenceCollector ?? new DiagnosticEvidenceCollector();
        }

        public async Task<DoctorActionPlan> TranslateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                throw new ArgumentException("Describe the problem first.", nameof(prompt));

            var intent = await _inferenceEngine.PredictIntentAsync(prompt, cancellationToken);
            var evidence = await _evidenceCollector.CollectEvidenceAsync(intent, cancellationToken);
            var findings = new List<DiagnosticFinding>();
            var proposedSteps = new List<ProposedActionStep>();
            DiagnosticSeverity severity = DiagnosticSeverity.Information;
            string summary;

            var pressureEvidence = evidence.FirstOrDefault(item => item.IndicatesPressure && item.HasMeasuredEvidence);
            bool hasMeasuredPressure = pressureEvidence is not null;

            switch (intent)
            {
                case "intent.storage.cleanup":
                    severity = hasMeasuredPressure ? DiagnosticSeverity.Warning : DiagnosticSeverity.Information;
                    summary = hasMeasuredPressure
                        ? $"Your system drive is running low on space ({pressureEvidence!.MeasuredValue})."
                        : "This sounds like a storage problem. Start by checking what is using space.";
                    findings.Add(new DiagnosticFinding(
                        "finding.storage.temp",
                        hasMeasuredPressure ? "System drive is low on space" : "Storage may need cleanup",
                        hasMeasuredPressure
                            ? $"Windows reports {pressureEvidence!.MeasuredValue}."
                            : "Check temporary files and other common space users before deleting anything.",
                        severity,
                        "Storage (C:)",
                        IsVerifiedByTelemetry: hasMeasuredPressure));
                    AddRecommendedSteps(proposedSteps,
                        "cleaner-preview-cards",
                        "cleanup-targets",
                        "storage",
                        "cleaner-disk-pressure");
                    break;

                case "intent.memory.optimize":
                    severity = hasMeasuredPressure ? DiagnosticSeverity.Warning : DiagnosticSeverity.Information;
                    summary = hasMeasuredPressure
                        ? $"Memory use is high ({pressureEvidence!.MeasuredValue})."
                        : "This sounds like a memory pressure problem. Check current memory use before making changes.";
                    findings.Add(new DiagnosticFinding(
                        "finding.memory.standby",
                        hasMeasuredPressure ? "Memory use is high" : "Memory use may be contributing to the slowdown",
                        hasMeasuredPressure
                            ? $"Windows reports {pressureEvidence!.MeasuredValue}."
                            : "Check current memory load and working sets before clearing caches.",
                        severity,
                        "System memory",
                        IsVerifiedByTelemetry: hasMeasuredPressure));
                    AddRecommendedSteps(proposedSteps, "internals-memory", "health", "system");
                    break;

                case "intent.network.flush":
                    summary = "This sounds like a network or DNS problem. Start with the read-only network checks.";
                    findings.Add(new DiagnosticFinding(
                        "finding.network.dns",
                        "Network or DNS may be the cause",
                        "Check DNS resolution and network state before resetting anything.",
                        DiagnosticSeverity.Information,
                        "Network",
                        IsVerifiedByTelemetry: false));
                    AddRecommendedSteps(proposedSteps, "network", "network-measure", "tcp-global");
                    break;

                case "intent.privacy.harden":
                    summary = "Review Windows privacy and diagnostic settings before changing them.";
                    findings.Add(new DiagnosticFinding(
                        "finding.privacy.telemetry",
                        "Privacy settings may need a review",
                        "Check the current Windows diagnostic and privacy settings first.",
                        DiagnosticSeverity.Information,
                        "Privacy settings",
                        IsVerifiedByTelemetry: false));
                    AddRecommendedSteps(proposedSteps,
                        "security-controls",
                        "telemetry-snapshot",
                        "experience-privacy-profiles",
                        "experience-privacy-apply");
                    break;

                case "intent.apps.update":
                    summary = "Check Windows Update history and available updates.";
                    findings.Add(new DiagnosticFinding(
                        "finding.apps.outdated",
                        "Updates may be available",
                        "Check the current update list before installing anything.",
                        DiagnosticSeverity.Information,
                        "Windows Update",
                        IsVerifiedByTelemetry: false));
                    AddRecommendedSteps(proposedSteps, "wua-search", "wua-history");
                    break;

                default:
                    summary = "Start with a general system check.";
                    findings.Add(new DiagnosticFinding(
                        "finding.general.inquiry",
                        "A general check is a good place to start",
                        "The description alone isn't enough to tell what's wrong. Check the system first, then decide what to do.",
                        DiagnosticSeverity.Information,
                        "System",
                        IsVerifiedByTelemetry: false));
                    AddRecommendedSteps(proposedSteps, "system", "storage", "security");
                    break;
            }

            if (proposedSteps.Count == 0)
            {
                var fallback = _catalogService.All.FirstOrDefault(command => command.ReadOnly &&
                    command.MigrationStatus is MigrationStatus.Implemented or MigrationStatus.BehaviorVerified);
                if (fallback is not null) proposedSteps.Add(CreateStep(fallback));
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

        private void AddRecommendedSteps(List<ProposedActionStep> steps, params string[] commandIds)
        {
            foreach (string id in commandIds)
            {
                var match = _catalogService.All.FirstOrDefault(command => command.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (match is null || match.MigrationStatus is not (MigrationStatus.Implemented or MigrationStatus.BehaviorVerified)) continue;
                if (steps.Any(step => step.CommandId.Equals(match.Id, StringComparison.OrdinalIgnoreCase))) continue;

                steps.Add(CreateStep(match));
                if (steps.Count >= 5) return;
            }
        }

        private static ProposedActionStep CreateStep(CommandDefinition match) =>
            new(
                CommandId: match.Id,
                Title: match.Title,
                Description: match.Summary,
                RiskLevel: match.Risk,
                IsReadOnly: match.ReadOnly,
                AccessRequirement: match.AdministratorAccess,
                Parameters: DefaultParameters(match),
                AffectedResource: match.Area);

        private static IReadOnlyDictionary<string, string>? DefaultParameters(CommandDefinition command) =>
            command.Id switch
            {
                "cleaner-disk-pressure" => new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["OlderThanDays"] = "7"
                },
                "experience-privacy-apply" => new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["IncludeTelemetry"] = "false"
                },
                _ => null
            };
    }
}
