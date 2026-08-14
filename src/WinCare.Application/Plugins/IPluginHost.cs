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
    /// Gets all dynamically registered plugin commands.
    /// </summary>
    IReadOnlyCollection<CommandDefinition> RegisteredCommands { get; }

    /// <summary>
    /// Registers a runtime tool command dynamically from a plugin. Returns true if registered, false if rejected due to validation or collision.
    /// </summary>
    bool RegisterCommand(CommandDefinition command, ICommandHandler? handler = null);

    /// <summary>
    /// Unregisters a runtime tool command dynamically.
    /// </summary>
    void UnregisterCommand(string commandId);
}
