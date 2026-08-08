using WinCare.CommandCatalog.Models;

namespace WinCare.Application.Tools;

/// <summary>
/// Filters for <see cref="ToolCatalogService.Search"/>.
/// </summary>
/// <param name="Area">Area filter, or <c>null</c> for all areas.</param>
/// <param name="Risk">Risk filter, or <c>null</c> for all risks.</param>
/// <param name="ReadOnly">Read-only filter, or <c>null</c> for any kind.</param>
public sealed record ToolFilter(
    string? Area = null,
    CommandRisk? Risk = null,
    bool? ReadOnly = null)
{
    /// <summary>
    /// Gets the unfiltered filter (all areas, risks, and read-only states).
    /// </summary>
    public static ToolFilter All { get; } = new();
}
