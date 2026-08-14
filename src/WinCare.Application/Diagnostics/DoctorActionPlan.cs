using System;
using System.Collections.Generic;
using System.Linq;
using WinCare.CommandCatalog.Models;

namespace WinCare.Application.Diagnostics
{
    public enum DiagnosticSeverity
    {
        Healthy = 0,
        Information = 1,
        Warning = 2,
        Critical = 3
    }

    public sealed record DiagnosticFinding(
        string Id,
        string Title,
        string Description,
        DiagnosticSeverity Severity,
        string AffectedResource,
        bool IsVerifiedByTelemetry = false
    );

    public sealed record ProposedActionStep(
        string CommandId,
        string Title,
        string Description,
        CommandRisk RiskLevel,
        bool RequiresElevation,
        IReadOnlyDictionary<string, string>? Parameters = null,
        string AffectedResource = "System",
        bool UndoAvailable = false
    )
    {
        public bool IsReadOnly => RiskLevel == CommandRisk.ReadOnly;
        public string RiskBadgeText => IsReadOnly ? "SAFE / READ-ONLY" : $"{RiskLevel.ToString().ToUpperInvariant()} RISK";
        public string ElevationBadgeText => RequiresElevation ? "ADMIN REQUIRED" : "STANDARD USER";
        public string ActionButtonText => IsReadOnly ? "Run Diagnostic Check" : "Review & Apply Fix";
    }

    public sealed class DoctorActionPlan
    {
        public required string PlanId { get; init; }
        public required string NaturalLanguageQuery { get; init; }
        public required string DiagnosisSummary { get; init; }
        public required DiagnosticSeverity OverallSeverity { get; init; }
        public required IReadOnlyList<DiagnosticFinding> Findings { get; init; }
        public required IReadOnlyList<ProposedActionStep> ProposedSteps { get; init; }
        public bool HasMutatingActions => ProposedSteps.Any(s => s.RiskLevel != CommandRisk.ReadOnly);
        public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
    }
}
