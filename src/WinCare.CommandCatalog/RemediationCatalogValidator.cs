using System.Text.Json;
using System.Text.RegularExpressions;
using WinCare.CommandCatalog.Models;

namespace WinCare.CommandCatalog;

internal static class RemediationCatalogValidator
{
    internal const int ExpectedRuleCount = 69;
    internal const int ExpectedPresetCount = 7;

    private static readonly Regex RuleIdPattern = new(
        "^[a-z0-9][a-z0-9._-]{2,100}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PresetIdPattern = new(
        "^[a-z0-9][a-z0-9._-]{2,80}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex TagPattern = new(
        "^[A-Za-z0-9._-]{1,80}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SourceRecordPattern = new(
        "^[A-Za-z0-9._:-]{2,160}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> KnownActionTypes = new(StringComparer.Ordinal)
    {
        "SetDefenderPreference",
        "SetFirewallProfileState",
        "SetLocalUserState",
        "SetOptionalFeatureState",
        "SetPowerScheme",
        "SetRegistryValue",
        "SetServiceStartMode",
    };

    public static void ValidateRulesDocument(JsonElement root)
    {
        ValidateObject(root, ["schemaVersion", "rules"], ["schemaVersion", "rules"], "catalog");
        RequireSchemaVersion(root, "catalog");
        JsonElement rules = RequireArray(root, "rules", "catalog");
        if (rules.GetArrayLength() != ExpectedRuleCount)
        {
            throw new InvalidOperationException(
                $"The built-in remediation catalog must contain exactly {ExpectedRuleCount} rules.");
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement rule in rules.EnumerateArray())
        {
            ValidateRule(rule, seen);
        }
    }

    public static void ValidatePresetsDocument(JsonElement root, IReadOnlySet<string> ruleIds)
    {
        ArgumentNullException.ThrowIfNull(ruleIds);
        ValidateObject(root, ["schemaVersion", "presets"], ["schemaVersion", "presets"], "presets");
        RequireSchemaVersion(root, "presets");
        JsonElement presets = RequireArray(root, "presets", "presets");
        if (presets.GetArrayLength() != ExpectedPresetCount)
        {
            throw new InvalidOperationException(
                $"The built-in preset catalog must contain exactly {ExpectedPresetCount} presets.");
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement preset in presets.EnumerateArray())
        {
            string[] keys = ["id", "title", "description", "ruleIds"];
            ValidateObject(preset, keys, keys, "preset");
            string id = RequireString(preset, "id", "preset");
            if (!PresetIdPattern.IsMatch(id) || !seen.Add(id))
            {
                throw new InvalidOperationException($"Invalid or duplicate preset ID '{id}'.");
            }
            RequireNonBlankString(preset, "title", id);
            RequireNonBlankString(preset, "description", id);
            JsonElement references = RequireArray(preset, "ruleIds", id);
            if (references.GetArrayLength() == 0)
            {
                throw new InvalidOperationException($"Preset '{id}' does not reference any rules.");
            }
            foreach (JsonElement reference in references.EnumerateArray())
            {
                string ruleId = RequireString(reference, $"rule reference in '{id}'");
                if (!ruleIds.Contains(ruleId))
                {
                    throw new InvalidOperationException($"Preset '{id}' references unknown rule '{ruleId}'.");
                }
            }
        }
    }

    private static void ValidateRule(JsonElement rule, ISet<string> seen)
    {
        string[] keys =
        [
            "id", "title", "description", "category", "risk", "requiresAdmin", "reversible",
            "restartPossible", "tags", "sourceRecords", "compatibility", "changes", "recovery",
        ];
        ValidateObject(rule, keys, keys, "catalog rule");

        string id = RequireString(rule, "id", "catalog rule");
        if (!RuleIdPattern.IsMatch(id) || !seen.Add(id))
        {
            throw new InvalidOperationException($"Invalid or duplicate remediation rule ID '{id}'.");
        }

        RequireNonBlankString(rule, "title", id);
        RequireNonBlankString(rule, "description", id);
        RequireNonBlankString(rule, "category", id);
        RequireNonBlankString(rule, "recovery", id);
        string risk = RequireString(rule, "risk", id);
        if (!Enum.TryParse<RemediationRisk>(risk, ignoreCase: false, out _))
        {
            throw new InvalidOperationException($"Invalid risk '{risk}' in remediation rule '{id}'.");
        }

        RequireBoolean(rule, "requiresAdmin", id);
        RequireBoolean(rule, "reversible", id);
        RequireBoolean(rule, "restartPossible", id);
        ValidateStringArray(RequireArray(rule, "tags", id), TagPattern, $"tags in '{id}'", requireNonEmpty: true);
        ValidateStringArray(
            RequireArray(rule, "sourceRecords", id),
            SourceRecordPattern,
            $"source records in '{id}'",
            requireNonEmpty: true);
        ValidateCompatibility(rule.GetProperty("compatibility"), id);

        JsonElement changes = RequireArray(rule, "changes", id);
        if (changes.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"Remediation rule '{id}' has no changes.");
        }
        foreach (JsonElement change in changes.EnumerateArray())
        {
            ValidateChange(change, id);
        }
    }

    private static void ValidateCompatibility(JsonElement compatibility, string ruleId)
    {
        ValidateObject(
            compatibility,
            ["minBuild", "maxBuild", "requires"],
            ["minBuild"],
            $"compatibility in '{ruleId}'");
        int minBuild = RequireInt32(compatibility, "minBuild", ruleId);
        if (minBuild < 10240)
        {
            throw new InvalidOperationException($"Remediation rule '{ruleId}' has an invalid minimum build.");
        }
        if (compatibility.TryGetProperty("maxBuild", out JsonElement maxBuildElement))
        {
            int maxBuild = RequireInt32(maxBuildElement, $"maxBuild in '{ruleId}'");
            if (maxBuild < minBuild)
            {
                throw new InvalidOperationException($"Remediation rule '{ruleId}' has an invalid build range.");
            }
        }
        if (compatibility.TryGetProperty("requires", out JsonElement requirements))
        {
            ValidateStringArray(requirements, TagPattern, $"requirements in '{ruleId}'", requireNonEmpty: true);
        }
    }

    private static void ValidateChange(JsonElement change, string ruleId)
    {
        ValidateObject(
            change,
            ["type", "parameters", "verification", "compensator", "preconditions", "postconditions"],
            ["type", "parameters"],
            $"change in '{ruleId}'");
        string type = RequireString(change, "type", ruleId);
        if (!KnownActionTypes.Contains(type))
        {
            throw new InvalidOperationException(
                $"Remediation rule '{ruleId}' references unsupported action type '{type}'.");
        }
        if (change.GetProperty("parameters").ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Remediation rule '{ruleId}' has invalid change parameters.");
        }
        if (change.TryGetProperty("verification", out JsonElement verification) &&
            (verification.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(verification.GetString())))
        {
            throw new InvalidOperationException($"Remediation rule '{ruleId}' has an invalid verification statement.");
        }
        if (change.TryGetProperty("compensator", out JsonElement compensator) &&
            compensator.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Remediation rule '{ruleId}' has an invalid compensator.");
        }
        ValidateOptionalArray(change, "preconditions", ruleId);
        ValidateOptionalArray(change, "postconditions", ruleId);
    }

    private static void ValidateObject(
        JsonElement element,
        IReadOnlyCollection<string> allowedKeys,
        IReadOnlyCollection<string> requiredKeys,
        string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Expected an object for {context}.");
        }

        HashSet<string> allowed = new(allowedKeys, StringComparer.Ordinal);
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new InvalidOperationException($"Duplicate property '{property.Name}' in {context}.");
            }
            if (!allowed.Contains(property.Name))
            {
                throw new InvalidOperationException($"Unknown property '{property.Name}' in {context}.");
            }
        }
        foreach (string required in requiredKeys)
        {
            if (!seen.Contains(required))
            {
                throw new InvalidOperationException($"Missing property '{required}' in {context}.");
            }
        }
    }

    private static void RequireSchemaVersion(JsonElement root, string context)
    {
        if (RequireInt32(root, "schemaVersion", context) != 1)
        {
            throw new InvalidOperationException($"Unsupported {context} schema.");
        }
    }

    private static JsonElement RequireArray(JsonElement owner, string propertyName, string context)
    {
        JsonElement value = owner.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Property '{propertyName}' in {context} must be an array.");
        }
        return value;
    }

    private static string RequireString(JsonElement owner, string propertyName, string context) =>
        RequireString(owner.GetProperty(propertyName), $"'{propertyName}' in {context}");

    private static string RequireString(JsonElement value, string context)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Expected a string for {context}.");
        }
        return value.GetString() ?? string.Empty;
    }

    private static void RequireNonBlankString(JsonElement owner, string propertyName, string context)
    {
        if (string.IsNullOrWhiteSpace(RequireString(owner, propertyName, context)))
        {
            throw new InvalidOperationException($"Property '{propertyName}' in {context} cannot be blank.");
        }
    }

    private static int RequireInt32(JsonElement owner, string propertyName, string context) =>
        RequireInt32(owner.GetProperty(propertyName), $"'{propertyName}' in {context}");

    private static int RequireInt32(JsonElement value, string context)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
        {
            throw new InvalidOperationException($"Expected an integer for {context}.");
        }
        return result;
    }

    private static void RequireBoolean(JsonElement owner, string propertyName, string context)
    {
        JsonValueKind kind = owner.GetProperty(propertyName).ValueKind;
        if (kind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new InvalidOperationException($"Property '{propertyName}' in {context} must be a Boolean.");
        }
    }

    private static void ValidateStringArray(
        JsonElement array,
        Regex pattern,
        string context,
        bool requireNonEmpty)
    {
        if (array.ValueKind != JsonValueKind.Array || (requireNonEmpty && array.GetArrayLength() == 0))
        {
            throw new InvalidOperationException($"Expected a non-empty string array for {context}.");
        }
        foreach (JsonElement item in array.EnumerateArray())
        {
            string value = RequireString(item, context);
            if (!pattern.IsMatch(value))
            {
                throw new InvalidOperationException($"Invalid value '{value}' in {context}.");
            }
        }
    }

    private static void ValidateOptionalArray(JsonElement owner, string propertyName, string context)
    {
        if (owner.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Property '{propertyName}' in '{context}' must be an array.");
        }
    }
}
