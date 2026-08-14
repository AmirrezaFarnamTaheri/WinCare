namespace WinCare.Application.Plugins;

using System;
using System.IO;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;

/// <summary>
/// Default implementation of <see cref="IPluginHost"/> providing environment directories and command dispatcher integration.
/// </summary>
public class DefaultPluginHost : IPluginHost
{
    private readonly ICommandDispatcher? _commandDispatcher;

    /// <summary>
    /// Initializes a new instance of <see cref="DefaultPluginHost"/>.
    /// </summary>
    public DefaultPluginHost(ICommandDispatcher? commandDispatcher = null, string? applicationRootPath = null, string? pluginsUserDirectory = null)
    {
        _commandDispatcher = commandDispatcher;
        ApplicationRootPath = applicationRootPath ?? AppDomain.CurrentDomain.BaseDirectory;

        if (string.IsNullOrWhiteSpace(pluginsUserDirectory))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            PluginsUserDirectory = Path.Combine(localAppData, "WinCare", "Plugins");
        }
        else
        {
            PluginsUserDirectory = pluginsUserDirectory;
        }

        Directory.CreateDirectory(PluginsUserDirectory);
    }

    /// <summary>
    /// Root path of the host application.
    /// </summary>
    public string ApplicationRootPath { get; }

    /// <summary>
    /// Directory path where user-installed plugins are stored.
    /// </summary>
    public string PluginsUserDirectory { get; }

    /// <summary>
    /// Command dispatcher instance for executing plugin commands.
    /// </summary>
    public ICommandDispatcher CommandDispatcher => _commandDispatcher ?? throw new InvalidOperationException("No CommandDispatcher has been configured for this PluginHost.");

    /// <summary>
    /// Dynamically registers a command from a plugin.
    /// </summary>
    public void RegisterCommand(CommandDefinition command)
    {
        // Dynamic command registration hook
    }

    /// <summary>
    /// Dynamically unregisters a command.
    /// </summary>
    public void UnregisterCommand(string commandId)
    {
        // Dynamic command unregistration hook
    }
}
