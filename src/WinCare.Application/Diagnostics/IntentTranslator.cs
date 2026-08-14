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
        private readonly IOnnxInferenceEngine _inferenceEngine;
        private readonly ToolCatalogService _catalogService;
        private readonly IDiagnosticEvidenceCollector _evidenceCollector;

        public IntentTranslator(
            IOnnxInferenceEngine inferenceEngine, 
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

                    AddMatchingCommands(proposedSteps, new[] { "clean_temp", "clean_updates", "clean_recycle_bin", "clean_dns" });
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

                    AddMatchingCommands(proposedSteps, new[] { "clear_standby_list", "flush_working_sets", "restart_explorer" });
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

                    AddMatchingCommands(proposedSteps, new[] { "flush_dns", "reset_winsock", "renew_ip" });
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

                    AddMatchingCommands(proposedSteps, new[] { "disable_telemetry", "disable_cortana", "disable_ad_id" });
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

                    AddMatchingCommands(proposedSteps, new[] { "winget_upgrade_all", "winget_source_update" });
                    break;

                default:
                    severity = DiagnosticSeverity.Healthy;
                    summary = "Inferred area of interest: General system inquiry. Recommended diagnostic inspection checks are available.";
                    findings.Add(new DiagnosticFinding(
                        "finding.general.healthy",
                        "System Inquiry Interpreted (No Immediate Issue Detected)",
                        "Query interpreted without requiring immediate system remediation. You may run routine diagnostic checks.",
                        DiagnosticSeverity.Healthy,
                        "System",
                        IsVerifiedByTelemetry: false
                    ));
                    break;
            }

            // Fallback: If no specific keywords mapped to existing catalog IDs, provide safest general command
            if (proposedSteps.Count == 0 && severity != DiagnosticSeverity.Healthy)
            {
                var fallback = _catalogService.All.FirstOrDefault(c => c.ReadOnly) ?? _catalogService.All.FirstOrDefault();
                if (fallback != null)
                {
                    proposedSteps.Add(new ProposedActionStep(
                        fallback.Id,
                        fallback.Title,
                        fallback.Summary,
                        fallback.Risk,
                        fallback.AdministratorAccess == AdministratorAccess.Required
                    ));
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

        private void AddMatchingCommands(List<ProposedActionStep> steps, string[] queryTokens)
        {
            foreach (var token in queryTokens)
            {
                var matches = _catalogService.Search(token);
                // Prioritize read-only inspection commands first to provide verification evidence before mutations
                foreach (var match in matches.OrderByDescending(m => m.ReadOnly))
                {
                    if (!steps.Any(s => s.CommandId.Equals(match.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        steps.Add(new ProposedActionStep(
                            CommandId: match.Id,
                            Title: match.Title,
                            Description: match.Summary,
                            RiskLevel: match.Risk,
                            RequiresElevation: match.AdministratorAccess == AdministratorAccess.Required,
                            AffectedResource: match.Area,
                            UndoAvailable: match.Risk != CommandRisk.Critical
                        ));
                        if (steps.Count >= 5) return; // Cap recommendation steps to top 5
                    }
                }
            }
        }
    }
}
