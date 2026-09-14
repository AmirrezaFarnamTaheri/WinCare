using WinCare.CommandCatalog.Models;
using WinCare.Domain.Activity;

namespace WinCare.Application.Tools;

/// <summary>A read-only pairing of catalog capability and historical execution evidence.</summary>
public sealed record CareToolProjection(CommandDefinition Command, ActivityRecord? LatestActivity);

/// <summary>Projects existing evidence without probing the machine or granting execution authority.</summary>
public static class CareAreaProjectionService
{
    /// <summary>Projects catalog commands into care-page rows.</summary>
    public static IReadOnlyList<CareToolProjection> Project(
        ToolCatalogService catalog,
        CareAreaSelection selection,
        IReadOnlyList<ActivityRecord> history)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(history);

        var latest = history
            .GroupBy(record => record.CommandId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(record => record.StartedAt).First(),
                StringComparer.OrdinalIgnoreCase);

        return catalog.All
            .Where(command => selection.Matches(command.Area, command.Section))
            .OrderBy(command => command.RiskTier)
            .ThenBy(command => command.Title, StringComparer.OrdinalIgnoreCase)
            .Select(command => new CareToolProjection(command, latest.GetValueOrDefault(command.Id)))
            .ToArray();
    }

    /// <summary>
    /// Search projection retained for command-centric callers. Product care pages should use
    /// the structured overload above.
    /// </summary>
    public static IReadOnlyList<CareToolProjection> Project(
        ToolCatalogService catalog,
        string query,
        IReadOnlyList<ActivityRecord> history)
    {
        var latest = history
            .GroupBy(record => record.CommandId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(record => record.StartedAt).First(),
                StringComparer.OrdinalIgnoreCase);
        string[] terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return catalog.Search(query)
            .OrderByDescending(command => terms.Any(term => command.Title.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(command => command.RiskTier)
            .ThenBy(command => command.Title, StringComparer.OrdinalIgnoreCase)
            .Select(command => new CareToolProjection(command, latest.GetValueOrDefault(command.Id)))
            .ToArray();
    }
}
