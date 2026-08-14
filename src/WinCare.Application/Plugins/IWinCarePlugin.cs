namespace WinCare.Application.Plugins;

using WinCare.CommandCatalog;

/// <summary>
/// Contract implemented by compiled C# WinCare plugins (.dll).
/// </summary>
public interface IWinCarePlugin
{
    /// <summary>
    /// Unique reverse-domain package identifier (e.g., com.wincare.plugins.disk_cleaner).
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Display name of the plugin.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// SemVer version string.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Author or publisher name.
    /// </summary>
    string Author { get; }

    /// <summary>
    /// Brief description of the plugin features.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Initializes the plugin with the host environment.
    /// </summary>
    Task InitializeAsync(IPluginHost host, CancellationToken ct = default);

    /// <summary>
    /// Returns all command definitions exposed by this plugin.
    /// </summary>
    IReadOnlyList<CommandDefinition> GetCommands();

    /// <summary>
    /// Returns all dynamic dashboard widgets exposed by this plugin.
    /// </summary>
    IReadOnlyList<IPluginWidget> GetWidgets();
}
