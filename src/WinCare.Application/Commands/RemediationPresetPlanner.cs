using System.Text.Json;
using System.Collections.Immutable;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands;

/// <summary>
/// Resolves a remediation preset into a fixed, reviewable set of rule changes before any mutation begins.
/// </summary>
public static class RemediationPresetPlanner
{
    public static RemediationPresetPlan Create(
        string presetId,
        IReadOnlyList<PresetDefinition> presets,
        IReadOnlyList<RemediationRule> rules,
        int windowsBuild,
        bool isAdministrator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetId);
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentNullException.ThrowIfNull(rules);

        PresetDefinition? preset = presets.FirstOrDefault(x => x.Id.Equals(presetId, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            return RemediationPresetPlan.Invalid(presetId, $"Preset '{presetId}' does not exist.");
        }

        IReadOnlyDictionary<string, RemediationRule> byId = rules.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var resolved = new List<ResolvedRemediationRule>(preset.RuleIds.Count);
        var failures = new List<string>();
        foreach (string ruleId in preset.RuleIds)
        {
            if (!byId.TryGetValue(ruleId, out RemediationRule? rule))
            {
                failures.Add($"Preset '{preset.Id}' references missing rule '{ruleId}'.");
                continue;
            }

            if (windowsBuild < rule.Compatibility.MinBuild)
            {
                failures.Add($"Rule '{rule.Id}' requires Windows build {rule.Compatibility.MinBuild} or newer.");
            }
            else if (rule.Compatibility.MaxBuild.HasValue && windowsBuild > rule.Compatibility.MaxBuild.Value)
            {
                failures.Add($"Rule '{rule.Id}' is only supported through Windows build {rule.Compatibility.MaxBuild.Value}.");
            }

            if (rule.RequiresAdmin && !isAdministrator)
            {
                failures.Add($"Rule '{rule.Id}' requires administrator access.");
            }

            resolved.Add(new ResolvedRemediationRule(
                rule.Id,
                rule.Title,
                rule.Description,
                rule.Category,
                rule.Risk.ToString(),
                rule.RequiresAdmin,
                rule.RestartPossible,
                rule.Reversible,
                rule.Recovery,
                rule.Changes.Select(change => new ResolvedRemediationChange(
                    change.Type,
                    change.Parameters.Clone(),
                    change.Verification,
                    change.Compensator?.Clone())).ToImmutableArray()));
        }

        JsonElement payload = JsonSerializer.SerializeToElement(new
        {
            presetId = preset.Id,
            rules = resolved.Select(rule => new
            {
                rule.Id,
                rule.Changes,
            }),
        });
        string digest = ApprovedMutationPlan.ComputeCanonicalDigest(payload);
        return new RemediationPresetPlan(preset.Id, preset.Title, resolved.ToImmutableArray(), failures.ToImmutableArray(), digest);
    }
}

/// <summary>Immutable expanded preset data suitable for review and execution.</summary>
public sealed record RemediationPresetPlan(
    string PresetId,
    string Title,
    ImmutableArray<ResolvedRemediationRule> Rules,
    ImmutableArray<string> PreflightFailures,
    string Digest)
{
    public bool IsExecutable => PreflightFailures.Length == 0;

    internal static RemediationPresetPlan Invalid(string presetId, string failure) =>
        new(presetId, presetId, ImmutableArray<ResolvedRemediationRule>.Empty, ImmutableArray.Create(failure), string.Empty);
}

public sealed record ResolvedRemediationRule(
    string Id,
    string Title,
    string Description,
    string Category,
    string Risk,
    bool RequiresAdmin,
    bool RestartPossible,
    bool Reversible,
    string Recovery,
    ImmutableArray<ResolvedRemediationChange> Changes);

public sealed record ResolvedRemediationChange(
    string Type,
    JsonElement Parameters,
    string? Verification,
    JsonElement? Compensator);
