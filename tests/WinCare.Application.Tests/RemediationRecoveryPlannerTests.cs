using System.Text.Json;
using WinCare.Application.Commands;

namespace WinCare.Application.Tests;

public sealed class RemediationRecoveryPlannerTests
{
    [Fact]
    public void Completed_registry_changes_produce_reverse_order_compensators()
    {
        JsonElement history = JsonSerializer.SerializeToElement(new
        {
            id = "run-1", status = "Applied", changes = new object[]
            {
                new { Type = "SetRegistryValue", detail = new { path = @"HKCU:\A", name = "One", previous = 1, previousKind = "DWord", value = 0, valueType = "DWord" } },
                new { Type = "SetRegistryValue", detail = new { path = @"HKCU:\B", name = "Two", previous = (object?)null, previousKind = (string?)null, value = "new", valueType = "String" } },
            }
        });
        RemediationRecoveryPlan plan = RemediationRecoveryPlanner.Create(history);
        Assert.True(plan.IsExecutable);
        Assert.Equal("run-1", plan.ExecutionId);
        Assert.Equal(["Two", "One"], plan.Steps.Select(step => step.Name));
        Assert.Equal(64, plan.Digest.Length);
    }

    [Fact]
    public void Mixed_or_incomplete_history_fails_closed()
    {
        JsonElement mixed = JsonSerializer.SerializeToElement(new { id = "run-2", status = "Applied", changes = new[] { new { Type = "SetServiceStartMode", detail = new { } } } });
        Assert.False(RemediationRecoveryPlanner.Create(mixed).IsExecutable);
        JsonElement partial = JsonSerializer.SerializeToElement(new { id = "run-3", status = "PartiallyApplied", changes = new[] { new { Type = "SetRegistryValue", detail = new { } } } });
        Assert.False(RemediationRecoveryPlanner.Create(partial).IsExecutable);
    }

    [Fact]
    public void Digest_is_stable_when_receipt_properties_are_reordered()
    {
        using JsonDocument first = JsonDocument.Parse("""{"id":"run-4","status":"Applied","changes":[{"Type":"SetRegistryValue","detail":{"path":"HKCU:\\\\A","name":"One","previous":1,"previousKind":"DWord","value":0,"valueType":"DWord"}}]}""");
        using JsonDocument reordered = JsonDocument.Parse("""{"changes":[{"detail":{"valueType":"DWord","value":0,"previousKind":"DWord","previous":1,"name":"One","path":"HKCU:\\\\A"},"Type":"SetRegistryValue"}],"status":"Applied","id":"run-4"}""");

        Assert.Equal(
            RemediationRecoveryPlanner.Create(first.RootElement).Digest,
            RemediationRecoveryPlanner.Create(reordered.RootElement).Digest);
    }
}
