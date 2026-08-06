namespace WinCare.CommandCatalog.Models;

/// <summary>
/// Risk tier declared by a catalog command.
/// </summary>
public enum CommandRisk
{
    ReadOnly,
    Low,
    Moderate,
    High,
    Critical,
}

/// <summary>
/// Administrator access requirement.
/// </summary>
public enum AdministratorAccess
{
    No,
    MayBeRequired,
    Required,
}

/// <summary>
/// Whether the command may require a restart.
/// </summary>
public enum RestartExpectation
{
    No,
    MayBeRequired,
    Required,
}

/// <summary>
/// Migration lifecycle of a catalog command.
/// </summary>
public enum MigrationStatus
{
    Cataloged,
    ContractVerified,
    Implemented,
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
