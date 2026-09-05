using System.Collections.Concurrent;
using System.Linq;

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
    private readonly ConcurrentDictionary<string, CommandDefinition> _registeredCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _registrationLock = new();

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

    /// <summary>
    /// Gets all dynamically registered plugin commands.
    /// </summary>
    public IReadOnlyCollection<CommandDefinition> RegisteredCommands => _registeredCommands.Values.ToList().AsReadOnly();

    /// <inheritdoc />
    public ICommandDispatcher CommandDispatcher => _commandDispatcher ?? throw new InvalidOperationException("No CommandDispatcher has been configured for this PluginHost.");

    /// <inheritdoc />
    public bool RegisterCommand(CommandDefinition command, ICommandHandler? handler = null)
    {
        lock (_registrationLock)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.Id))
            {
                return false;
            }

            if (_registeredCommands.ContainsKey(command.Id))
            {
                return false;
            }

            if (_commandDispatcher != null)
            {
                if (handler == null)
                {
                    // Dynamic commands require an executable handler to prevent unexecutable ghost tools
                    return false;
                }

                if (!_commandDispatcher.RegisterDynamicCommand(command, handler))
                {
                    return false;
                }
            }

            _registeredCommands[command.Id] = command;
            return true;
        }
    }

    /// <inheritdoc />
    public void UnregisterCommand(string commandId)
    {
        lock (_registrationLock)
        {
            if (!string.IsNullOrWhiteSpace(commandId))
            {
                _registeredCommands.TryRemove(commandId, out _);
                _commandDispatcher?.UnregisterDynamicCommand(commandId);
            }
        }
    }
}
