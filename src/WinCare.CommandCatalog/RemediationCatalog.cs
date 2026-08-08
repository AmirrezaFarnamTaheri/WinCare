using System.Reflection;
using System.Text.Json;
using WinCare.CommandCatalog.Models;

namespace WinCare.CommandCatalog;

/// <summary>
/// Loads and validates the embedded remediation and preset catalogs.
/// </summary>
public static class RemediationCatalog
{
    private const string RulesResourceName = "WinCare.CommandCatalog.Data.remediation-rules.json";
    private const string PresetsResourceName = "WinCare.CommandCatalog.Data.presets.json";
    private const int MaximumResourceBytes = 4 * 1024 * 1024;

    private static readonly Lazy<IReadOnlyList<RemediationRule>> Rules = new(LoadRulesCore);
    private static readonly Lazy<IReadOnlyList<PresetDefinition>> Presets = new(LoadPresetsCore);

    /// <summary>
    /// Returns the validated remediation rule catalog.
    /// </summary>
    public static IReadOnlyList<RemediationRule> LoadRules() => Rules.Value;

    /// <summary>
    /// Returns the validated preset catalog.
    /// </summary>
    public static IReadOnlyList<PresetDefinition> LoadPresets() => Presets.Value;

    /// <summary>
    /// Serializes the validated rules as a <see cref="JsonElement"/> array.
    /// </summary>
    public static JsonElement SerializeRules() =>
        JsonSerializer.SerializeToElement(
            LoadRules().ToArray(),
            RemediationCatalogJsonContext.Default.RemediationRuleArray);

    /// <summary>
    /// Serializes the validated presets as a <see cref="JsonElement"/> array.
    /// </summary>
    public static JsonElement SerializePresets() =>
        JsonSerializer.SerializeToElement(
            LoadPresets().ToArray(),
            RemediationCatalogJsonContext.Default.PresetDefinitionArray);

    private static IReadOnlyList<RemediationRule> LoadRulesCore()
    {
        using JsonDocument document = EmbeddedJsonResource.Read(
            Assembly.GetExecutingAssembly(),
            RulesResourceName,
            MaximumResourceBytes);
        RemediationCatalogValidator.ValidateRulesDocument(document.RootElement);
        RemediationCatalogDocument typed = document.RootElement.Deserialize(
            RemediationCatalogJsonContext.Default.RemediationCatalogDocument)
            ?? throw new InvalidOperationException("The embedded remediation catalog is empty.");
        return Array.AsReadOnly(typed.Rules.ToArray());
    }

    private static IReadOnlyList<PresetDefinition> LoadPresetsCore()
    {
        using JsonDocument document = EmbeddedJsonResource.Read(
            Assembly.GetExecutingAssembly(),
            PresetsResourceName,
            MaximumResourceBytes);
        IReadOnlySet<string> ruleIds = new HashSet<string>(
            LoadRules().Select(rule => rule.Id),
            StringComparer.OrdinalIgnoreCase);
        RemediationCatalogValidator.ValidatePresetsDocument(document.RootElement, ruleIds);
        PresetCatalogDocument typed = document.RootElement.Deserialize(
            RemediationCatalogJsonContext.Default.PresetCatalogDocument)
            ?? throw new InvalidOperationException("The embedded preset catalog is empty.");
        return Array.AsReadOnly(typed.Presets.ToArray());
    }
}
