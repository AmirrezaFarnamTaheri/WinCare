using WinCare.Application.Plugins;
using WinCare.CommandCatalog.Models;

namespace WinCare.Application.Tools;

/// <summary>
/// Filters and searches the frozen command catalog.
/// </summary>
public sealed class ToolCatalogService
{
    private readonly IReadOnlyList<CommandDefinition> _commands;

    /// <summary>
    /// Initializes the service against the embedded catalog and optional plugin registry.
    /// </summary>
    public ToolCatalogService(IPluginRegistry? pluginRegistry = null)
    {
        var baseCommands = CommandCatalog.CommandCatalog.Load();
        if (pluginRegistry == null)
        {
            _commands = baseCommands;
        }
        else
        {
            var activePluginCommands = pluginRegistry.GetActivePluginCommands();
            var merged = new Dictionary<string, CommandDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var cmd in baseCommands)
            {
                merged[cmd.Id] = cmd;
            }
            foreach (var cmd in activePluginCommands)
            {
                merged[cmd.Id] = cmd;
            }
            _commands = merged.Values.ToList();
        }
    }

    /// <summary>
    /// Initializes the service against a supplied command set.
    /// </summary>
    public ToolCatalogService(IReadOnlyList<CommandDefinition> commands)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    /// <summary>
    /// Gets the complete command set.
    /// </summary>
    public IReadOnlyList<CommandDefinition> All => _commands;

    /// <summary>
    /// Searches and filters commands by area, risk, read-only status, and query.
    /// </summary>
    public IReadOnlyList<CommandDefinition> Search(string? query, ToolFilter? filter = null)
    {
        filter ??= ToolFilter.All;
        string normalizedQuery = query?.Trim() ?? string.Empty;

        IEnumerable<CommandDefinition> result = _commands;

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
