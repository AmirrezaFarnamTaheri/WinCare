namespace WinCare.Application.Plugins;

using System.Collections.Concurrent;
using WinCare.CommandCatalog.Models;

/// <summary>
/// Core implementation of IPluginRegistry discovering, isolating, and managing plugin state.
/// </summary>
public sealed class PluginRegistryService : IPluginRegistry
{
    private readonly ConcurrentDictionary<string, PluginRegistryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (IWinCarePlugin Plugin, PluginLoadContext? LoadContext)> _instantiatedPlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly IPluginStateRepository? _stateRepository;
    private readonly HashSet<string> _enabledIds;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of <see cref="PluginRegistryService"/>.
    /// </summary>
    public PluginRegistryService(IPluginStateRepository? stateRepository = null, HashSet<string>? initialEnabledPluginIds = null)
    {
        _stateRepository = stateRepository;
        _enabledIds = initialEnabledPluginIds ?? _stateRepository?.LoadEnabledPluginIds() ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task DiscoverAndInitializeAsync(IPluginHost host, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _entries.Clear();
            foreach (var kvp in _instantiatedPlugins)
            {
                try
                {
                    await kvp.Value.Plugin.ShutdownAsync(ct).ConfigureAwait(false);
                    await kvp.Value.Plugin.DisposeAsync().ConfigureAwait(false);
                }
                catch { }
                kvp.Value.LoadContext?.Unload();
            }
            _instantiatedPlugins.Clear();

            // Discover built-in plugins from embedded resources
            DiscoverEmbeddedBuiltInPlugins();

            // Scan built-in plugins in ApplicationRootPath/Plugins
            var builtInDir = Path.Combine(host.ApplicationRootPath, "Plugins");
            if (Directory.Exists(builtInDir))
            {
                foreach (var dir in Directory.GetDirectories(builtInDir))
                {
                    LoadPluginDirectory(dir, isBuiltIn: true);
                }

                foreach (var file in Directory.GetFiles(builtInDir, "*.json"))
                {
                    LoadPluginJsonFile(file, isBuiltIn: true);
                }
            }

            // Scan user-installed plugins in PluginsUserDirectory
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
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<PluginRegistryEntry> GetAllPlugins() => _entries.Values.ToList();

    /// <inheritdoc />
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

    /// <inheritdoc />
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
                    var pluginWidgets = kvp.Value.Plugin.GetWidgets();
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

    /// <inheritdoc />
    public async Task EnablePluginAsync(string pluginId, IPluginHost host, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnablePluginInternalAsync(pluginId, host, ct);

            if (_entries.TryGetValue(pluginId, out var entry) && entry.State == PluginState.Enabled)
            {
                _enabledIds.Add(pluginId);
                _stateRepository?.SaveEnabledPluginIds(_enabledIds);
            }
            else
            {
                _enabledIds.Remove(pluginId);
                _stateRepository?.SaveEnabledPluginIds(_enabledIds);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task DisablePluginAsync(string pluginId, IPluginHost host, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _enabledIds.Remove(pluginId);
            _stateRepository?.SaveEnabledPluginIds(_enabledIds);

            if (_entries.TryGetValue(pluginId, out var entry))
            {
                foreach (var cmd in entry.Commands)
                {
                    host.UnregisterCommand(cmd.Id);
                }

                _entries[pluginId] = entry with { State = PluginState.Disabled };
            }

            if (_instantiatedPlugins.TryRemove(pluginId, out var inst))
            {
                try
                {
                    await inst.Plugin.ShutdownAsync(ct).ConfigureAwait(false);
                    await inst.Plugin.DisposeAsync().ConfigureAwait(false);
                }
                catch { }
                inst.LoadContext?.Unload();
            }
        }
        finally
        {
            _gate.Release();
        }
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
    }

    private void InstantiateAssemblyPlugin(PluginRegistryEntry entry, PluginManifest manifest)
    {
        var canonicalDir = Path.GetFullPath(entry.SourceDirectoryPath);
        var assemblyPath = Path.GetFullPath(Path.Combine(canonicalDir, manifest.AssemblyFileName!));
        var relativePath = Path.GetRelativePath(canonicalDir, assemblyPath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            _entries[manifest.Id] = entry with { State = PluginState.Error, ErrorMessage = "Security Violation: Assembly file path traverses outside plugin directory." };
            return;
        }

        var asmResult = AssemblyPluginLoader.LoadPluginAssembly(assemblyPath, manifest.PluginClassName);
        if (asmResult.Success && asmResult.Plugin != null)
        {
            _instantiatedPlugins[manifest.Id] = (asmResult.Plugin, asmResult.LoadContext);
        }
        else if (!asmResult.Success)
        {
            _entries[manifest.Id] = entry with { State = PluginState.Error, ErrorMessage = asmResult.ErrorMessage ?? "Assembly load failed." };
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
            if (!_instantiatedPlugins.ContainsKey(pluginId) && !string.IsNullOrEmpty(entry.SourceDirectoryPath))
            {
                var loadResult = JsonPluginLoader.LoadFromDirectory(entry.SourceDirectoryPath);
                if (loadResult.Success && loadResult.Manifest != null &&
                    loadResult.Manifest.EntryType.Equals("Assembly", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(loadResult.Manifest.AssemblyFileName))
                {
                    InstantiateAssemblyPlugin(entry, loadResult.Manifest);
                    if (!_instantiatedPlugins.ContainsKey(pluginId))
                    {
                        return;
                    }
                }
            }

            if (_instantiatedPlugins.TryGetValue(pluginId, out var inst))
            {
                // Host Exception Isolation Boundary: Wrap third-party InitializeAsync in exception handler
                await inst.Plugin.InitializeAsync(host, ct);
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

    private void DiscoverEmbeddedBuiltInPlugins()
    {
        try
        {
            var assembly = typeof(CommandCatalog.CommandCatalog).Assembly;
            var resourceNames = assembly.GetManifestResourceNames()
                .Where(r => r.Contains("BuiltIn") && r.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

            foreach (var resName in resourceNames)
            {
                using var stream = assembly.GetManifestResourceStream(resName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var json = reader.ReadToEnd();
                    var loadResult = JsonPluginLoader.LoadFromString(json, string.Empty);
                    if (loadResult.Success && loadResult.Manifest != null)
                    {
                        var manifest = loadResult.Manifest;
                        var entry = new PluginRegistryEntry(
                            Id: manifest.Id,
                            Name: manifest.Name,
                            Version: manifest.Version,
                            Author: manifest.Author,
                            Description: manifest.Description,
                            Category: manifest.Category,
                            SourceDirectoryPath: string.Empty,
                            IsBuiltIn: true,
                            State: PluginState.Disabled,
                            Commands: loadResult.Commands,
                            ErrorMessage: loadResult.ErrorMessage
                        );
                        _entries[manifest.Id] = entry;
                    }
                }
            }
        }
        catch
        {
            // Built-in embedded discovery best-effort
        }
    }

    private void LoadPluginJsonFile(string jsonFilePath, bool isBuiltIn)
    {
        try
        {
            if (!File.Exists(jsonFilePath)) return;
            var json = File.ReadAllText(jsonFilePath);
            var dirPath = Path.GetDirectoryName(jsonFilePath) ?? string.Empty;
            var loadResult = JsonPluginLoader.LoadFromString(json, dirPath);
            if (loadResult.Success && loadResult.Manifest != null)
            {
                var manifest = loadResult.Manifest;
                var initialState = isBuiltIn ? PluginState.Disabled : (_enabledIds.Contains(manifest.Id) ? PluginState.Enabled : PluginState.Disabled);
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
            }
        }
        catch
        {
            // Best effort file load
        }
    }
}
