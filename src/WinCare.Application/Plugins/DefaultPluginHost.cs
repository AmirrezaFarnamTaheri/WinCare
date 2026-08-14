namespace WinCare.Application.Plugins;

using System;
using System.IO;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;

public class DefaultPluginHost : IPluginHost
{
    private readonly ICommandDispatcher? _commandDispatcher;

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

    public string ApplicationRootPath { get; }

    public string PluginsUserDirectory { get; }

    public ICommandDispatcher CommandDispatcher => _commandDispatcher ?? throw new InvalidOperationException("No CommandDispatcher has been configured for this PluginHost.");

    public void RegisterCommand(CommandDefinition command)
    {
        // Dynamic command registration hook
    }

    public void UnregisterCommand(string commandId)
    {
        // Dynamic command unregistration hook
    }
}
