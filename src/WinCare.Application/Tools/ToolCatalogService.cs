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

    private readonly object _syncLock = new();
    private IReadOnlyList<CommandDefinition>? _cachedMergedCommands;

    /// <summary>
    /// Event raised when the merged catalog changes (e.g. plugins enabled or disabled).
    /// </summary>
    public event EventHandler? CatalogChanged;

    /// <summary>
    /// Initializes the service against the embedded catalog and optional plugin registry.
    /// </summary>
    public ToolCatalogService(IPluginRegistry? pluginRegistry = null)
    {
        _pluginRegistry = pluginRegistry;
        if (_pluginRegistry != null)
        {
            _pluginRegistry.RegistryChanged += (s, e) =>
            {
                lock (_syncLock)
                {
                    _cachedMergedCommands = null;
                }
                CatalogChanged?.Invoke(this, EventArgs.Empty);
            };
        }
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

        lock (_syncLock)
        {
            if (_cachedMergedCommands != null)
            {
                return _cachedMergedCommands;
            }

            IReadOnlyList<CommandDefinition> activePluginCommands = _pluginRegistry.GetActivePluginCommands();
            Dictionary<string, CommandDefinition> merged = new(StringComparer.OrdinalIgnoreCase);

            // Built-in core commands take absolute precedence
            foreach (CommandDefinition cmd in _baseCommands)
            {
                merged[cmd.Id] = cmd;
            }

            // Finding 5: Reserve core namespaces; do not overwrite core command definitions with plugin commands
            foreach (CommandDefinition cmd in activePluginCommands)
            {
                if (!merged.ContainsKey(cmd.Id))
                {
                    merged[cmd.Id] = cmd;
                }
            }

            _cachedMergedCommands = merged.Values.ToList();
            return _cachedMergedCommands;
        }
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
