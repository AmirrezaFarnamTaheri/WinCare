namespace WinCare.Application.Plugins;

using System.Collections.Concurrent;
using WinCare.CommandCatalog;

/// <summary>
/// Core implementation of IPluginRegistry discovering, isolating, and managing plugin state.
/// </summary>
public sealed class PluginRegistryService : IPluginRegistry
{
    private readonly ConcurrentDictionary<string, PluginRegistryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IWinCarePlugin> _instantiatedPlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _enabledIds;

    public PluginRegistryService(HashSet<string>? initialEnabledPluginIds = null)
    {
        _enabledIds = initialEnabledPluginIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task DiscoverAndInitializeAsync(IPluginHost host, CancellationToken ct = default)
    {
        _entries.Clear();
        _instantiatedPlugins.Clear();

        if (Directory.Exists(host.PluginsUserDirectory))
        {
            var pluginDirectories = Directory.GetDirectories(host.PluginsUserDirectory);
            foreach (var dir in pluginDirectories)
            {
                LoadPluginDirectory(dir, isBuiltIn: false);
            }
        }

        foreach (var entry in _entries.Values)
        {
            if (_enabledIds.Contains(entry.Id) || entry.IsBuiltIn)
            {
                await EnablePluginInternalAsync(entry.Id, host, ct);
            }
        }
    }

    public IReadOnlyList<PluginRegistryEntry> GetAllPlugins() => _entries.Values.ToList();

    public IReadOnlyList<CommandDefinition> GetActivePluginCommands()
    {
        var activeCommands = new List<CommandDefinition>();
        foreach (var entry in _entries.Values)
        {
            if (entry.State == PluginState.Enabled)
            {
                activeCommands.AddRange(entry.Commands);
            }
        }
        return activeCommands;
    }

    public IReadOnlyList<IPluginWidget> GetActivePluginWidgets()
    {
        var widgets = new List<IPluginWidget>();
        foreach (var kvp in _instantiatedPlugins)
        {
            if (_entries.TryGetValue(kvp.Key, out var entry) && entry.State == PluginState.Enabled)
            {
                try
                {
                    // Host Exception Isolation Boundary: Wrap third-party GetWidgets() in exception handler
                    var pluginWidgets = kvp.Value.GetWidgets();
                    if (pluginWidgets != null)
                    {
                        widgets.AddRange(pluginWidgets);
                    }
                }
                catch (Exception ex)
                {
                    _entries[kvp.Key] = entry with
                    {
                        State = PluginState.Error,
                        ErrorMessage = $"Widget retrieval failed: {ex.Message}"
                    };
                }
            }
        }
        return widgets;
    }

    public async Task EnablePluginAsync(string pluginId, IPluginHost host, CancellationToken ct = default)
    {
        _enabledIds.Add(pluginId);
        await EnablePluginInternalAsync(pluginId, host, ct);
    }

    public Task DisablePluginAsync(string pluginId, IPluginHost host, CancellationToken ct = default)
    {
        _enabledIds.Remove(pluginId);
        if (_entries.TryGetValue(pluginId, out var entry))
        {
            foreach (var cmd in entry.Commands)
            {
                host.UnregisterCommand(cmd.Id);
            }

            _entries[pluginId] = entry with { State = PluginState.Disabled };
        }

        return Task.CompletedTask;
    }

    private void LoadPluginDirectory(string dirPath, bool isBuiltIn)
    {
        var loadResult = JsonPluginLoader.LoadFromDirectory(dirPath);
        if (!loadResult.Success || loadResult.Manifest == null)
        {
            return;
        }

        var manifest = loadResult.Manifest;
        var initialState = _enabledIds.Contains(manifest.Id) || isBuiltIn ? PluginState.Enabled : PluginState.Disabled;

        var entry = new PluginRegistryEntry(
            Id: manifest.Id,
            Name: manifest.Name,
            Version: manifest.Version,
            Author: manifest.Author,
            Description: manifest.Description,
            Category: manifest.Category,
            SourceDirectoryPath: dirPath,
            IsBuiltIn: isBuiltIn,
            State: initialState,
            Commands: loadResult.Commands,
            ErrorMessage: loadResult.ErrorMessage
        );

        _entries[manifest.Id] = entry;

        if (manifest.EntryType.Equals("Assembly", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(manifest.AssemblyFileName))
        {
            var assemblyPath = Path.Combine(dirPath, manifest.AssemblyFileName);
            var asmResult = AssemblyPluginLoader.LoadPluginAssembly(assemblyPath, manifest.PluginClassName);
            if (asmResult.Success && asmResult.Plugin != null)
            {
                _instantiatedPlugins[manifest.Id] = asmResult.Plugin;
            }
        }
    }

    private async Task EnablePluginInternalAsync(string pluginId, IPluginHost host, CancellationToken ct)
    {
        if (!_entries.TryGetValue(pluginId, out var entry))
        {
            return;
        }

        try
        {
            if (_instantiatedPlugins.TryGetValue(pluginId, out var plugin))
            {
                // Host Exception Isolation Boundary: Wrap third-party InitializeAsync in exception handler
                await plugin.InitializeAsync(host, ct);
            }

            foreach (var cmd in entry.Commands)
            {
                host.RegisterCommand(cmd);
            }

            _entries[pluginId] = entry with { State = PluginState.Enabled, ErrorMessage = null };
        }
        catch (Exception ex)
        {
            _entries[pluginId] = entry with { State = PluginState.Error, ErrorMessage = $"Initialization failed: {ex.Message}" };
        }
    }
}
