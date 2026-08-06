using System.Text.Json;
using WinCare.CommandCatalog.Models;

namespace WinCare.CommandCatalog.Tests;

public sealed class RemediationCatalogTests
{
    [Fact]
    public void Load_rules_returns_the_69_unique_built_in_rules()
    {
        IReadOnlyList<RemediationRule> rules = RemediationCatalog.LoadRules();

        Assert.Equal(69, rules.Count);
        Assert.Equal(69, rules.Select(rule => rule.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(rules, rule => Assert.NotEmpty(rule.Changes));
    }

    [Fact]
    public void Load_presets_returns_seven_presets_with_known_rule_references()
    {
        IReadOnlySet<string> ruleIds = RemediationCatalog.LoadRules()
            .Select(rule => rule.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<PresetDefinition> presets = RemediationCatalog.LoadPresets();

        Assert.Equal(7, presets.Count);
        Assert.All(presets, preset =>
        {
            Assert.NotEmpty(preset.RuleIds);
            Assert.All(preset.RuleIds, ruleId => Assert.Contains(ruleId, ruleIds));
        });
    }

    [Fact]
    public void Serialized_results_use_the_legacy_array_shape_and_omit_absent_optional_values()
    {
        JsonElement rules = RemediationCatalog.SerializeRules();
        JsonElement presets = RemediationCatalog.SerializePresets();

        Assert.Equal(JsonValueKind.Array, rules.ValueKind);
        Assert.Equal(JsonValueKind.Array, presets.ValueKind);
        JsonElement firstRule = rules.EnumerateArray().First();
        Assert.True(firstRule.TryGetProperty("id", out _));
        Assert.True(firstRule.TryGetProperty("changes", out _));
        Assert.False(firstRule.GetProperty("compatibility").TryGetProperty("requires", out _));
    }
}
