using WinCare.Application.Diagnostics;
using Xunit;

namespace WinCare.Application.Tests;

/// <summary>
/// Regression tests for network evidence collection without fixed observations.
/// </summary>
public sealed class NetworkEvidenceTests
{
    private const string FabricatedObservation = "Active Socket Table and DNS Entries Present";

    [Fact]
    public async Task Network_intent_never_emits_the_fixed_healthy_observation()
    {
        var collector = new DiagnosticEvidenceCollector();
        var evidence = await collector.CollectEvidenceAsync("intent.network.flush");

        Assert.NotEmpty(evidence);
        Assert.All(evidence, item => Assert.NotEqual(FabricatedObservation, item.MeasuredValue));
    }

    [Fact]
    public async Task Measured_network_claims_reflect_a_real_adapter_snapshot()
    {
        var collector = new DiagnosticEvidenceCollector();
        var evidence = await collector.CollectEvidenceAsync("intent.network.flush");

        foreach (var item in evidence.Where(e => e.HasMeasuredEvidence))
        {
            Assert.Matches(@"^\d+ of \d+ non-loopback adapters operational$", item.MeasuredValue);
            Assert.Equal(item.IndicatesPressure, item.MeasuredValue.StartsWith("0 of ", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Healthy_and_disconnected_snapshots_produce_distinct_results()
    {
        var healthy = DiagnosticEvidenceCollector.NetworkEvidenceFromSnapshot(operationalCount: 2, upCount: 2);
        var disconnected = DiagnosticEvidenceCollector.NetworkEvidenceFromSnapshot(operationalCount: 2, upCount: 0);

        Assert.True(healthy[0].HasMeasuredEvidence);
        Assert.True(disconnected[0].HasMeasuredEvidence);
        Assert.NotEqual(healthy[0].MeasuredValue, disconnected[0].MeasuredValue);
        Assert.NotEqual(healthy[0].Severity, disconnected[0].Severity);
        Assert.False(healthy[0].IndicatesPressure);
        Assert.True(disconnected[0].IndicatesPressure);
    }

    [Fact]
    public void No_operational_adapters_is_reported_as_unavailable_not_measured()
    {
        var none = DiagnosticEvidenceCollector.NetworkEvidenceFromSnapshot(operationalCount: 0, upCount: 0);

        Assert.False(none[0].HasMeasuredEvidence);
        Assert.False(none[0].IndicatesPressure);
        Assert.NotEqual(FabricatedObservation, none[0].MeasuredValue);
    }
}
