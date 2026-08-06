namespace WinCare.CommandCatalog.Models;

/// <summary>
/// Typed preset that bundles remediation rule IDs.
/// </summary>
/// <param name="Id">Stable preset ID.</param>
/// <param name="Title">Display title.</param>
/// <param name="Description">Plain-language description.</param>
/// <param name="RuleIds">Rule IDs included in this preset.</param>
public sealed record PresetDefinition(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<string> RuleIds);

internal sealed record PresetCatalogDocument(
    int SchemaVersion,
    IReadOnlyList<PresetDefinition> Presets);
