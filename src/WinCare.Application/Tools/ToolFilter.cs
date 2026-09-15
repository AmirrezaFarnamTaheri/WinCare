using WinCare.CommandCatalog.Models;

namespace WinCare.Application.Tools;

/// <summary>
/// Filters for <see cref="ToolCatalogService.Search"/>.
/// </summary>
/// <param name="Area">Area filter, or <c>null</c> for all areas.</param>
/// <param name="Section">Section filter, or <c>null</c> for all sections.</param>
/// <param name="Risk">Risk filter, or <c>null</c> for all risks.</param>
/// <param name="ReadOnly">Read-only filter, or <c>null</c> for any kind.</param>
public sealed record ToolFilter(
    string? Area = null,
    string? Section = null,
    CommandRisk? Risk = null,
    bool? ReadOnly = null)
{
    public static ToolFilter All { get; } = new();
}
