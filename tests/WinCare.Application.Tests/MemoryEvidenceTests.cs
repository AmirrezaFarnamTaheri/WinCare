using WinCare.Application.Diagnostics;
using Xunit;

namespace WinCare.Application.Tests;

public sealed class MemoryEvidenceTests
{
    [Fact]
    public void Physical_memory_readings_identify_machine_memory_not_process_gc()
    {
        TelemetryEvidence evidence = DiagnosticEvidenceCollector.MemoryEvidenceFromSnapshot(
            load: 75, totalBytes: 16_000_000_000, availableBytes: 4_000_000_000);
        Assert.True(evidence.HasMeasuredEvidence);
        Assert.Contains("System memory", evidence.MetricName);
        Assert.Contains("4.0 GB available", evidence.MeasuredValue);
        Assert.DoesNotContain("WinCare process", evidence.MeasuredValue);
    }

    [Fact]
    public void Missing_memory_measurement_is_unavailable_not_zero_usage()
    {
        TelemetryEvidence evidence = DiagnosticEvidenceCollector.MemoryEvidenceFromSnapshot(0, 0, 0);
        Assert.False(evidence.HasMeasuredEvidence);
        Assert.False(evidence.IndicatesPressure);
        Assert.Equal(DiagnosticSeverity.Information, evidence.Severity);
    }
}
