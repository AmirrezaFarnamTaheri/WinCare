namespace WinCare.Application.Plugins;

using System.Collections.Concurrent;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

/// <summary>
/// Delegate factory for constructing script-backed command handlers for declarative plugin tools.
/// </summary>
public delegate ICommandHandler ScriptCommandHandlerFactory(string commandId, string scriptRelativePath, string pluginDirectory);

/// <summary>
/// Core implementation of IPluginRegistry discovering, isolating, and managing plugin state.
/// </summary>
public sealed class PluginRegistryService : IPluginRegistry
{
    private readonly ConcurrentDictionary<string, PluginRegistryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (IWinCarePlugin Plugin, PluginLoadContext? LoadContext)> _instantiatedPlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly IPluginStateRepository? _stateRepository;
    private readonly HashSet<string> _enabledIds;
    private readonly ScriptCommandHandlerFactory? _scriptHandlerFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public event EventHandler? RegistryChanged;

    /// <summary>
    /// Initializes a new instance of <see cref="PluginRegistryService"/>.
    /// </summary>
    public PluginRegistryService(
        IPluginStateRepository? stateRepository = null,
        HashSet<string>? initialEnabledPluginIds = null,
        ScriptCommandHandlerFactory? scriptHandlerFactory = null)
    {
        _stateRepository = stateRepository;
        _enabledIds = initialEnabledPluginIds ?? _stateRepository?.LoadEnabledPluginIds() ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _scriptHandlerFactory = scriptHandlerFactory;
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
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PluginRegistry] Failed shutting down plugin '{kvp.Key}': {ex.GetType().Name} - {ex.Message}");
                }
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
                    if (IsIgnoredDirectory(dir)) continue;
                    LoadPluginDirectory(dir, isBuiltIn: true);
                }

                foreach (var file in Directory.GetFiles(builtInDir, "*.json"))
                {
                    LoadPluginJsonFile(file, isBuiltIn: true);
                }
            }

            // Scan user-installed plugins in PluginsUserDirectory (ignoring staging & backups)
            if (Directory.Exists(host.PluginsUserDirectory))
            {
                var pluginDirectories = Directory.GetDirectories(host.PluginsUserDirectory);
                foreach (var dir in pluginDirectories)
                {
                    if (IsIgnoredDirectory(dir)) continue;
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
            RegistryChanged?.Invoke(this, EventArgs.Empty);
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
            RegistryChanged?.Invoke(this, EventArgs.Empty);
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
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PluginRegistry] Failed shutting down plugin '{pluginId}': {ex.GetType().Name} - {ex.Message}");
                }
                inst.LoadContext?.Unload();
            }
        }
        finally
        {
            _gate.Release();
            RegistryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool IsIgnoredDirectory(string dirPath)
    {
        var name = Path.GetFileName(dirPath);
        return string.IsNullOrWhiteSpace(name) ||
               name.StartsWith(".", StringComparison.Ordinal) ||
               name.StartsWith("_", StringComparison.Ordinal) ||
               name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
               name.Contains(".bak.", StringComparison.OrdinalIgnoreCase);
    }

    private void LoadPluginDirectory(string dirPath, bool isBuiltIn)
    {
        var loadResult = JsonPluginLoader.LoadFromDirectory(dirPath);
        if (!loadResult.Success || loadResult.Manifest == null)
        {
            return;
        }

        var manifest = loadResult.Manifest;

        // Finding 5: Fail-closed duplicate & reserved namespace protection
        if (_entries.TryGetValue(manifest.Id, out var existing))
        {
            if (existing.IsBuiltIn && !isBuiltIn)
            {
                System.Diagnostics.Debug.WriteLine($"[PluginRegistry] Rejected user plugin '{manifest.Id}': cannot overwrite built-in plugin.");
                return;
            }
            if (!isBuiltIn)
            {
                System.Diagnostics.Debug.WriteLine($"[PluginRegistry] Duplicate user plugin '{manifest.Id}' rejected.");
                return;
            }
        }

        if (!isBuiltIn && (manifest.Id.StartsWith("wincare.core.", StringComparison.OrdinalIgnoreCase) || manifest.Id.StartsWith("system.", StringComparison.OrdinalIgnoreCase)))
        {
            System.Diagnostics.Debug.WriteLine($"[PluginRegistry] Rejected user plugin '{manifest.Id}': collides with reserved core namespace.");
            return;
        }

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

        var registeredCommandIds = new List<string>();
        try
        {
            PluginManifest? manifest = null;
            if (!string.IsNullOrEmpty(entry.SourceDirectoryPath))
            {
                var loadResult = JsonPluginLoader.LoadFromDirectory(entry.SourceDirectoryPath);
                if (loadResult.Success && loadResult.Manifest != null)
                {
                    manifest = loadResult.Manifest;
                    if (manifest.EntryType.Equals("Assembly", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(manifest.AssemblyFileName))
                    {
                        InstantiateAssemblyPlugin(entry, manifest);
                        if (!_instantiatedPlugins.ContainsKey(pluginId))
                        {
                            return;
                        }
                    }
                }
            }

            // Global command collision check & reserved namespace protection
            var existingRegistered = host.RegisteredCommands.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var cmd in entry.Commands)
            {
                if (!entry.IsBuiltIn && (cmd.Id.StartsWith("wincare.core.", StringComparison.OrdinalIgnoreCase) || cmd.Id.StartsWith("system.", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException($"Command '{cmd.Id}' collides with reserved core namespace.");
                }

                if (existingRegistered.Contains(cmd.Id))
                {
                    throw new InvalidOperationException($"Command ID '{cmd.Id}' is already registered by another plugin or host.");
                }
            }

            if (_instantiatedPlugins.TryGetValue(pluginId, out var inst))
            {
                await inst.Plugin.InitializeAsync(host, ct);
            }

            // Register plugin commands with host and dispatcher
            var toolMap = manifest?.Tools?.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var cmd in entry.Commands)
            {
                ICommandHandler? handler = null;
                if (toolMap != null && toolMap.TryGetValue(cmd.Id, out var toolDef) &&
                    !string.IsNullOrWhiteSpace(toolDef.ScriptPath) &&
                    !string.IsNullOrWhiteSpace(entry.SourceDirectoryPath) &&
                    _scriptHandlerFactory != null)
                {
                    handler = _scriptHandlerFactory(cmd.Id, toolDef.ScriptPath, entry.SourceDirectoryPath);
                }
                else if (entry.IsBuiltIn)
                {
                    var targetCoreId = ResolveBuiltInCoreCommandId(cmd.Id);
                    if (!string.IsNullOrWhiteSpace(targetCoreId))
                    {
                        try
                        {
                            handler = new BuiltInDelegatingHandler(cmd.Id, targetCoreId, host.CommandDispatcher);
                        }
                        catch
                        {
                            // If host has no dispatcher configured (e.g. mock host in unit tests), handler remains null
                        }
                    }
                }

                if (!host.RegisterCommand(cmd, handler))
                {
                    throw new InvalidOperationException($"Command registration for '{cmd.Id}' was rejected by the host.");
                }

                registeredCommandIds.Add(cmd.Id);
            }

            _entries[pluginId] = entry with { State = PluginState.Enabled, ErrorMessage = null };
        }
        catch (Exception ex)
        {
            // Transactional rollback of any commands registered during this failed attempt
            foreach (var cmdId in registeredCommandIds)
            {
                try { host.UnregisterCommand(cmdId); } catch { }
            }

            _entries[pluginId] = entry with { State = PluginState.Error, ErrorMessage = $"Initialization failed: {ex.Message}" };
        }
    }

    private static string? ResolveBuiltInCoreCommandId(string commandId)
    {
        return commandId.ToLowerInvariant() switch
        {
            "cleaner.system_temp" => "cleaner-disk-pressure",
            "cleaner.recycle_bin" => "cleaner-disk-pressure",
            "security.defender_status" => "security-defender-audit",
            _ => null
        };
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
                if (_entries.ContainsKey(manifest.Id) && !isBuiltIn) return;

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

internal sealed class BuiltInDelegatingHandler : ICommandHandler
{
    private readonly string _commandId;
    private readonly string _targetCoreCommandId;
    private readonly ICommandDispatcher _dispatcher;

    public BuiltInDelegatingHandler(string commandId, string targetCoreCommandId, ICommandDispatcher dispatcher)
    {
        _commandId = commandId;
        _targetCoreCommandId = targetCoreCommandId;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public string CommandId => _commandId;

    public async Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        var mappedRequest = new CommandRequest(
            _targetCoreCommandId,
            request.Parameters,
            request.Apply,
            request.CorrelationId,
            request.Approval
        );

        var options = request.Apply ? new CommandExecutionOptions(ReviewApproved: true) : CommandExecutionOptions.Default;
        var result = await _dispatcher.ExecuteAsync(mappedRequest, options, cancellationToken).ConfigureAwait(false);
        if (result.Status == CommandResultStatus.Succeeded)
        {
            return CommandHandlerOutcome.Succeeded(result.Code, result.Message, result.Data, result.UndoAvailable);
        }
        if (result.Status == CommandResultStatus.Blocked)
        {
            return CommandHandlerOutcome.Blocked(result.Code, result.Message);
        }
        return CommandHandlerOutcome.Failed(result.Code, result.Message, result.Data);
    }
}
