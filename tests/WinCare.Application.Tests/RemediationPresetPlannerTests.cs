using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;

namespace WinCare.Application.Tests;

public sealed class RemediationPresetPlannerTests
{
    [Fact]
    public void Create_ExpandsRulesInPresetOrder_AndComputesStableDigest()
    {
        PresetDefinition preset = new("workstation", "Workstation", "Test preset", ["first", "second"]);
        RemediationRule first = Rule("first", requiresAdmin: false, minBuild: 1, changeType: "SetRegistryValue");
        RemediationRule second = Rule("second", requiresAdmin: true, minBuild: 1, changeType: "SetServiceStartMode");

        RemediationPresetPlan firstPlan = RemediationPresetPlanner.Create("workstation", [preset], [first, second], 99_999, isAdministrator: true);
        RemediationPresetPlan secondPlan = RemediationPresetPlanner.Create("WORKSTATION", [preset], [first, second], 99_999, isAdministrator: true);

        Assert.True(firstPlan.IsExecutable);
        Assert.Equal(["first", "second"], firstPlan.Rules.Select(rule => rule.Id));
        Assert.Equal("SetRegistryValue", firstPlan.Rules[0].Changes[0].Type);
        Assert.Equal(firstPlan.Digest, secondPlan.Digest);
        Assert.NotEmpty(firstPlan.Digest);
    }

    [Fact]
    public void Create_ReportsAllPrerequisitesBeforeExecution()
    {
        PresetDefinition preset = new("blocked", "Blocked", "Test preset", ["admin", "future", "missing"]);
        RemediationRule admin = Rule("admin", requiresAdmin: true, minBuild: 1, changeType: "SetRegistryValue");
        RemediationRule future = Rule("future", requiresAdmin: false, minBuild: 50_000, changeType: "SetRegistryValue");

        RemediationPresetPlan plan = RemediationPresetPlanner.Create("blocked", [preset], [admin, future], 1, isAdministrator: false);

        Assert.False(plan.IsExecutable);
        Assert.Equal(3, plan.PreflightFailures.Length);
        Assert.Contains(plan.PreflightFailures, failure => failure.Contains("administrator", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.PreflightFailures, failure => failure.Contains("50000", StringComparison.Ordinal));
        Assert.Contains(plan.PreflightFailures, failure => failure.Contains("missing rule 'missing'", StringComparison.Ordinal));
    }

    private static RemediationRule Rule(string id, bool requiresAdmin, int minBuild, string changeType)
    {
        using JsonDocument parameters = JsonDocument.Parse("""{ "Path":"HKCU:\\Software\\WinCare", "Name":"Test", "Value":0, "ValueType":"DWord" }""");
        return new RemediationRule(
            id, id, id, "Test", RemediationRisk.Low, requiresAdmin, true, false,
            Array.Empty<string>(), Array.Empty<string>(), new RemediationCompatibility(minBuild, null),
            [new RemediationChange(changeType, parameters.RootElement.Clone(), null, null, null, null)], "Restore previous value.");
    }
}
