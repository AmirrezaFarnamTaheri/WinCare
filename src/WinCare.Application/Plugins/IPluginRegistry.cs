namespace WinCare.Application.Plugins;

using WinCare.CommandCatalog.Models;

/// <summary>
/// Status of a discovered plugin in the registry.
/// </summary>
public enum PluginState
{
    /// <summary>Plugin is currently disabled.</summary>
    Disabled,

    /// <summary>Plugin is currently enabled and active.</summary>
    Enabled,

    /// <summary>Plugin encountered an error during initialization.</summary>
    Error
}

/// <summary>
/// Domain model for an installed plugin entry in the registry.
/// </summary>
public sealed record PluginRegistryEntry(
    string Id,
    string Name,
    string Version,
    string Author,
    string Description,
    string Category,
    string SourceDirectoryPath,
    bool IsBuiltIn,
    PluginState State,
    IReadOnlyList<CommandDefinition> Commands,
    string? ErrorMessage
);

/// <summary>
/// Discovers, loads, and manages lifecycle state across built-in and user-installed plugins.
/// </summary>
public interface IPluginRegistry
{
    /// <summary>
    /// Event raised whenever plugins are discovered, enabled, or disabled.
    /// </summary>
    event EventHandler? RegistryChanged;

    /// <summary>
    /// Scans built-in and user directories (%LocalAppData%/WinCare/Plugins) and initializes enabled plugins.
    /// </summary>
    Task DiscoverAndInitializeAsync(IPluginHost host, CancellationToken ct = default);

    /// <summary>
    /// Returns all discovered plugins with their current state.
    /// </summary>
    IReadOnlyList<PluginRegistryEntry> GetAllPlugins();

    /// <summary>
    /// Aggregates all active command definitions exposed by currently enabled plugins.
    /// </summary>
    IReadOnlyList<CommandDefinition> GetActivePluginCommands();

    /// <summary>
    /// Aggregates all dynamic widgets exposed by currently enabled plugins.
    /// </summary>
    IReadOnlyList<IPluginWidget> GetActivePluginWidgets();

    /// <summary>
    /// Enables a plugin by ID and registers its commands.
    /// </summary>
    Task EnablePluginAsync(string pluginId, IPluginHost host, CancellationToken ct = default);

    /// <summary>
    /// Disables a plugin by ID and unregisters its commands.
    /// </summary>
    Task DisablePluginAsync(string pluginId, IPluginHost host, CancellationToken ct = default);
}
