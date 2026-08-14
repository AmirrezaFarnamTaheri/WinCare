namespace WinCare.Application.Plugins;

using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;

/// <summary>
/// Host context provided to plugins during initialization.
/// </summary>
public interface IPluginHost
{
    /// <summary>
    /// Root installation directory of the WinCare application.
    /// </summary>
    string ApplicationRootPath { get; }

    /// <summary>
    /// User plugins directory path (%LocalAppData%/WinCare/Plugins).
    /// </summary>
    string PluginsUserDirectory { get; }

    /// <summary>
    /// Global command dispatcher for executing system maintenance tasks.
    /// </summary>
    ICommandDispatcher CommandDispatcher { get; }

    /// <summary>
    /// Registers a runtime tool command dynamically from a plugin.
    /// </summary>
    void RegisterCommand(CommandDefinition command);

    /// <summary>
    /// Unregisters a runtime tool command dynamically.
    /// </summary>
    void UnregisterCommand(string commandId);
}
