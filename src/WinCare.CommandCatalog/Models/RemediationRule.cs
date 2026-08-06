using System.Text.Json;

namespace WinCare.CommandCatalog.Models;

/// <summary>
/// Risk tier for a remediation rule.
/// </summary>
public enum RemediationRisk
{
    Low,
    Moderate,
    High,
    Critical,
}

/// <summary>
/// Declared OS compatibility for a rule.
/// </summary>
/// <param name="MinBuild">Minimum Windows build.</param>
/// <param name="Requires">Optional feature requirements.</param>
public sealed record RemediationCompatibility(
    int MinBuild,
    IReadOnlyList<string>? Requires);

/// <summary>
/// A single typed mutation within a remediation rule.
/// </summary>
/// <param name="Type">Action type.</param>
/// <param name="Parameters">Action parameters.</param>
/// <param name="Verification">Verification statement.</param>
/// <param name="Compensator">Optional compensator payload.</param>
/// <param name="Preconditions">Optional preconditions.</param>
/// <param name="Postconditions">Optional postconditions.</param>
public sealed record RemediationChange(
    string Type,
    JsonElement Parameters,
    string? Verification,
    JsonElement? Compensator,
    IReadOnlyList<JsonElement>? Preconditions,
    IReadOnlyList<JsonElement>? Postconditions);

/// <summary>
/// Typed remediation rule from the native catalog.
/// </summary>
/// <param name="Id">Stable rule ID.</param>
/// <param name="Title">Display title.</param>
/// <param name="Description">Plain-language description.</param>
/// <param name="Category">Category label.</param>
/// <param name="Risk">Declared risk.</param>
/// <param name="RequiresAdmin">Whether administrator approval is required.</param>
/// <param name="Reversible">Whether the rule is reversible via compensator.</param>
/// <param name="RestartPossible">Whether a restart may be required.</param>
/// <param name="Tags">Search tags.</param>
/// <param name="SourceRecords">Provenance source records.</param>
/// <param name="Compatibility">OS compatibility declaration.</param>
/// <param name="Changes">Typed mutation sequence.</param>
/// <param name="Recovery">Recovery summary.</param>
public sealed record RemediationRule(
    string Id,
    string Title,
    string Description,
    string Category,
    RemediationRisk Risk,
    bool RequiresAdmin,
    bool Reversible,
    bool RestartPossible,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> SourceRecords,
    RemediationCompatibility Compatibility,
    IReadOnlyList<RemediationChange> Changes,
    string Recovery);

internal sealed record RemediationCatalogDocument(
    int SchemaVersion,
    IReadOnlyList<RemediationRule> Rules);
