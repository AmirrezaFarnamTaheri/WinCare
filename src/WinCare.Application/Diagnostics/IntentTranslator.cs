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

        public IntentTranslator(IOnnxInferenceEngine inferenceEngine, ToolCatalogService catalogService)
        {
            _inferenceEngine = inferenceEngine ?? throw new ArgumentNullException(nameof(inferenceEngine));
            _catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
        }

        public async Task<DoctorActionPlan> TranslateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("Prompt cannot be empty", nameof(prompt));
            }

            var intent = await _inferenceEngine.PredictIntentAsync(prompt, cancellationToken);
            var findings = new List<DiagnosticFinding>();
            var proposedSteps = new List<ProposedActionStep>();
            DiagnosticSeverity severity = DiagnosticSeverity.Information;
            string summary;

            switch (intent)
            {
                case "intent.storage.cleanup":
                    severity = DiagnosticSeverity.Warning;
                    summary = "Based on your storage query, recommended maintenance actions are available to inspect and reclaim temporary files and caches.";
                    findings.Add(new DiagnosticFinding(
                        "finding.storage.temp",
                        "Suggested Temporary File Maintenance",
                        "System and user temporary directories can be inspected for safe cache reclamation.",
                        DiagnosticSeverity.Warning,
                        "Storage (C:)"
                    ));

                    AddMatchingCommands(proposedSteps, new[] { "clean_temp", "clean_updates", "clean_recycle_bin", "clean_dns" });
                    break;

                case "intent.memory.optimize":
                    severity = DiagnosticSeverity.Warning;
                    summary = "Based on your memory query, recommended maintenance actions are available to evaluate working set and cache trimming.";
                    findings.Add(new DiagnosticFinding(
                        "finding.memory.standby",
                        "Suggested Memory Cache Evaluation",
                        "Memory working sets and standby lists can be trimmed to improve system responsiveness.",
                        DiagnosticSeverity.Warning,
                        "System RAM"
                    ));

                    AddMatchingCommands(proposedSteps, new[] { "clear_standby_list", "flush_working_sets", "restart_explorer" });
                    break;

                case "intent.network.flush":
                    severity = DiagnosticSeverity.Information;
                    summary = "Based on your network query, recommended diagnostic actions are available to refresh DNS resolver and socket states.";
                    findings.Add(new DiagnosticFinding(
                        "finding.network.dns",
                        "Suggested DNS Resolver Refresh",
                        "Local DNS resolver cache can be refreshed to eliminate resolution latency.",
                        DiagnosticSeverity.Information,
                        "Network Adapter"
                    ));

                    AddMatchingCommands(proposedSteps, new[] { "flush_dns", "reset_winsock", "renew_ip" });
                    break;

                case "intent.privacy.harden":
                    severity = DiagnosticSeverity.Information;
                    summary = "Based on your privacy query, recommended actions are available to review diagnostic telemetry settings.";
                    findings.Add(new DiagnosticFinding(
                        "finding.privacy.telemetry",
                        "Suggested Privacy Review",
                        "Standard Windows diagnostic data collection and advertising ID settings can be reviewed.",
                        DiagnosticSeverity.Information,
                        "Privacy Settings"
                    ));

                    AddMatchingCommands(proposedSteps, new[] { "disable_telemetry", "disable_cortana", "disable_ad_id" });
                    break;

                case "intent.apps.update":
                    severity = DiagnosticSeverity.Information;
                    summary = "Based on your application query, recommended actions are available to scan for software updates via WinGet.";
                    findings.Add(new DiagnosticFinding(
                        "finding.apps.outdated",
                        "Suggested Application Update Scan",
                        "Package repositories can be scanned for pending application updates.",
                        DiagnosticSeverity.Information,
                        "WinGet Package Manager"
                    ));

                    AddMatchingCommands(proposedSteps, new[] { "winget_upgrade_all", "winget_source_update" });
                    break;

                default:
                    severity = DiagnosticSeverity.Healthy;
                    summary = "General system assistance and recommended diagnostic checks based on your inquiry.";
                    findings.Add(new DiagnosticFinding(
                        "finding.general.healthy",
                        "System Inquiry Interpreted",
                        "Query interpreted without requiring immediate system remediation.",
                        DiagnosticSeverity.Healthy,
                        "System"
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
                ProposedSteps = proposedSteps
            };
        }

        private void AddMatchingCommands(List<ProposedActionStep> steps, string[] queryTokens)
        {
            foreach (var token in queryTokens)
            {
                var matches = _catalogService.Search(token);
                foreach (var match in matches)
                {
                    if (!steps.Any(s => s.CommandId.Equals(match.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        steps.Add(new ProposedActionStep(
                            match.Id,
                            match.Title,
                            match.Summary,
                            match.Risk,
                            match.AdministratorAccess == AdministratorAccess.Required
                        ));
                        if (steps.Count >= 5) return; // Cap recommendation steps to top 5
                    }
                }
            }
        }
    }
}
