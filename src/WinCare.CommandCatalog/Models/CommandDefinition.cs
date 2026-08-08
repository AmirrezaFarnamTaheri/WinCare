namespace WinCare.CommandCatalog.Models;

/// <summary>
/// Risk tier declared by a catalog command.
/// </summary>
public enum CommandRisk
{
    /// <summary>The command only reads system state.</summary>
    ReadOnly,
    /// <summary>The command has low operational risk.</summary>
    Low,
    /// <summary>The command has moderate operational risk.</summary>
    Moderate,
    /// <summary>The command has high operational risk.</summary>
    High,
    /// <summary>The command has critical operational risk.</summary>
    Critical,
}

/// <summary>
/// Administrator access requirement.
/// </summary>
public enum AdministratorAccess
{
    /// <summary>Administrator access is not required.</summary>
    No,
    /// <summary>Administrator access may be required.</summary>
    MayBeRequired,
    /// <summary>Administrator access is required.</summary>
    Required,
}

/// <summary>
/// Whether the command may require a restart.
/// </summary>
public enum RestartExpectation
{
    /// <summary>No restart is expected.</summary>
    No,
    /// <summary>A restart may be required.</summary>
    MayBeRequired,
    /// <summary>A restart is required.</summary>
    Required,
}

/// <summary>
/// Migration lifecycle of a catalog command.
/// </summary>
public enum MigrationStatus
{
    /// <summary>The legacy command is cataloged for migration.</summary>
    Cataloged,
    /// <summary>The command contract has been verified.</summary>
    ContractVerified,
    /// <summary>The native implementation is available.</summary>
    Implemented,
    /// <summary>The native behavior has been verified against the legacy command.</summary>
    BehaviorVerified,
}

/// <summary>
/// Single typed command definition from the native catalog.
/// </summary>
/// <param name="Id">Stable ID.</param>
/// <param name="Title">Display title.</param>
/// <param name="Summary">Plain-language summary.</param>
/// <param name="Area">Area navigation bucket.</param>
/// <param name="Section">Section within the area.</param>
/// <param name="Risk">Declared risk tier.</param>
/// <param name="ReadOnly">Whether the command never mutates host state.</param>
/// <param name="AdministratorAccess">Privilege requirement.</param>
/// <param name="Restart">Restart expectation.</param>
/// <param name="LegacySource">Legacy oracle source path for provenance.</param>
/// <param name="MigrationStatus">Current migration lifecycle state.</param>
/// <param name="Keywords">Search keywords.</param>
public sealed record CommandDefinition(
    string Id,
    string Title,
    string Summary,
    string Area,
    string Section,
    CommandRisk Risk,
    bool ReadOnly,
    AdministratorAccess AdministratorAccess,
    RestartExpectation Restart,
    string LegacySource,
    MigrationStatus MigrationStatus,
    IReadOnlyList<string> Keywords);

internal sealed record CommandCatalogDocument(
    int SchemaVersion,
    int CommandCount,
    IReadOnlyList<CommandDefinition> Commands);
