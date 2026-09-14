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
        public string RiskBadgeText => RiskLevel switch
        {
            CommandRisk.ReadOnly => "READ-ONLY",
            CommandRisk.Low => "SAFE",
            CommandRisk.Moderate => "MODERATE",
            CommandRisk.High or CommandRisk.Critical => "DESTRUCTIVE",
            _ => "SAFE",
        };
        public string ElevationBadgeText => RequiresElevation ? "ADMINISTRATOR REQUIRED" : "STANDARD ACCESS";
        public string ReviewContextText => $"{RiskBadgeText} · {ElevationBadgeText} · {AffectedResource}";
        public string ActionButtonText => IsReadOnly ? "Open check" : "Review in Power tools";
    }

    public sealed record TelemetryEvidence(
        bool HasMeasuredEvidence,
        string MetricName,
        string MeasuredValue,
        bool IndicatesPressure,
        DiagnosticSeverity Severity,
        string Source = "Windows System Diagnostic Telemetry",
        string? CommandId = null,
        DateTime? CapturedAtUtc = null,
        string Collector = "DiagnosticEvidenceCollector",
        string CommandVersion = "1.0.0"
    )
    {
        public DateTime TimestampUtc { get; init; } =
            CapturedAtUtc.HasValue ? NormalizeToUtc(CapturedAtUtc.Value) : DateTime.UtcNow;

        public bool IsStale(TimeSpan maxAge) => (DateTime.UtcNow - TimestampUtc.ToUniversalTime()) > maxAge;

        private static DateTime NormalizeToUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        public string ProvenanceSummary => $"Source: {Source} | Collector: {Collector} | Command: {CommandId ?? "system.core"}@{CommandVersion} | Captured: {TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC";
    };

    public sealed class DoctorActionPlan
    {
        public required string PlanId { get; init; }
        public required string NaturalLanguageQuery { get; init; }
        public required string DiagnosisSummary { get; init; }
        public required DiagnosticSeverity OverallSeverity { get; init; }
        public required IReadOnlyList<DiagnosticFinding> Findings { get; init; }
        public required IReadOnlyList<ProposedActionStep> ProposedSteps { get; init; }
        public IReadOnlyList<TelemetryEvidence> MeasuredEvidence { get; init; } = Array.Empty<TelemetryEvidence>();
        public bool HasMutatingActions => ProposedSteps.Any(s => s.RiskLevel != CommandRisk.ReadOnly);
        public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
    }
}
