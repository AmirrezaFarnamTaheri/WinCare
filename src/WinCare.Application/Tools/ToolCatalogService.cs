using System;
using System.Collections.Generic;
using System.Linq;
using WinCare.Application.Plugins;
using WinCare.CommandCatalog.Models;

namespace WinCare.Application.Tools;

/// <summary>
/// Filters and searches the frozen command catalog and active plugin commands dynamically.
/// </summary>
public sealed class ToolCatalogService
{
    private readonly IPluginRegistry? _pluginRegistry;
    private readonly IReadOnlyList<CommandDefinition> _baseCommands;

    /// <summary>
    /// Initializes the service against the embedded catalog and optional plugin registry.
    /// </summary>
    public ToolCatalogService(IPluginRegistry? pluginRegistry = null)
    {
        _pluginRegistry = pluginRegistry;
        _baseCommands = CommandCatalog.CommandCatalog.Load();
    }

    /// <summary>
    /// Initializes the service against a supplied command set.
    /// </summary>
    public ToolCatalogService(IReadOnlyList<CommandDefinition> commands)
    {
        _baseCommands = commands ?? throw new ArgumentNullException(nameof(commands));
        _pluginRegistry = null;
    }

    /// <summary>
    /// Gets the complete command set, dynamically merging active plugin commands.
    /// </summary>
    public IReadOnlyList<CommandDefinition> All => GetMergedCommands();

    /// <summary>
    /// Searches and filters commands by area, risk, read-only status, and query.
    /// </summary>
    public IReadOnlyList<CommandDefinition> Search(string? query, ToolFilter? filter = null)
    {
        filter ??= ToolFilter.All;
        string normalizedQuery = query?.Trim() ?? string.Empty;

        IEnumerable<CommandDefinition> result = GetMergedCommands();

        if (!string.IsNullOrWhiteSpace(filter.Area))
        {
            result = result.Where(command =>
                string.Equals(command.Area, filter.Area, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.Risk is not null)
        {
            result = result.Where(command => command.Risk == filter.Risk);
        }

        if (filter.ReadOnly is not null)
        {
            result = result.Where(command => command.ReadOnly == filter.ReadOnly);
        }

        if (normalizedQuery.Length > 0)
        {
            result = result.Where(command => Matches(command, normalizedQuery));
        }

        return result
            .OrderBy(command => command.Area, StringComparer.OrdinalIgnoreCase)
            .ThenBy(command => command.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<CommandDefinition> GetMergedCommands()
    {
        if (_pluginRegistry == null)
        {
            return _baseCommands;
        }

        var activePluginCommands = _pluginRegistry.GetActivePluginCommands();
        var merged = new Dictionary<string, CommandDefinition>(StringComparer.OrdinalIgnoreCase);

        // Built-in core commands take absolute precedence
        foreach (var cmd in _baseCommands)
        {
            merged[cmd.Id] = cmd;
        }

        // Finding 5: Reserve core namespaces; do not overwrite core command definitions with plugin commands
        foreach (var cmd in activePluginCommands)
        {
            if (!merged.ContainsKey(cmd.Id))
            {
                merged[cmd.Id] = cmd;
            }
        }

        return merged.Values.ToList();
    }

    private static bool Matches(CommandDefinition command, string query)
    {
        return command.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               command.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               command.Summary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               command.Area.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               command.Section.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               command.Keywords.Any(keyword => keyword.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
}
