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

    /// <inheritdoc />
    public string ApplicationRootPath { get; }

    /// <inheritdoc />
    public string PluginsUserDirectory { get; }

    /// <inheritdoc />
    public ICommandDispatcher CommandDispatcher => _commandDispatcher ?? throw new InvalidOperationException("No CommandDispatcher has been configured for this PluginHost.");

    /// <inheritdoc />
    public void RegisterCommand(CommandDefinition command)
    {
        // Dynamic command registration hook
    }

    /// <inheritdoc />
    public void UnregisterCommand(string commandId)
    {
        // Dynamic command unregistration hook
    }
}
