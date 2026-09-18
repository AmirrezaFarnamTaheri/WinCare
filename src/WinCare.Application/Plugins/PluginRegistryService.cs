namespace WinCare.Application.Plugins;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

/// <summary>
/// Delegate factory for constructing script-backed command handlers for declarative plugin tools.
/// The read-only flag and declared capabilities are passed through so the handler can enforce the
/// two-phase preview/approve gate and surface plugin-declared (advisory) capability metadata.
/// </summary>
public delegate ICommandHandler ScriptCommandHandlerFactory(
    string commandId,
    string scriptRelativePath,
    string pluginDirectory,
    bool readOnly,
    IReadOnlyList<string> declaredCapabilities);

public delegate ICommandHandler? BuiltInCommandHandlerFactory(CommandDefinition command);

/// <summary>
/// Core implementation of IPluginRegistry discovering, isolating, and managing plugin state.
/// </summary>
public sealed class PluginRegistryService : IPluginRegistry
{
    private readonly ConcurrentDictionary<string, PluginRegistryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PluginManifest> _builtInManifests = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (IWinCarePlugin Plugin, PluginLoadContext? LoadContext)> _instantiatedPlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _registeredCommandIdsByPlugin = new(StringComparer.OrdinalIgnoreCase);
    private readonly IPluginStateRepository? _stateRepository;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _widgetErrors =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _enabledIds;
    private readonly ScriptCommandHandlerFactory? _scriptHandlerFactory;
    private readonly BuiltInCommandHandlerFactory? _builtInHandlerFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Upper bound on a single plugin shutdown or disposal. A plugin that ignores its cancellation
    /// token is abandoned after this so plugin discovery, enable and disable cannot be held forever.
    /// </summary>
    private static readonly TimeSpan PluginShutdownTimeout = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public event EventHandler? RegistryChanged;

    /// <summary>
    /// Initializes a new instance of <see cref="PluginRegistryService"/>.
    /// </summary>
    public PluginRegistryService(
        IPluginStateRepository? stateRepository = null,
        HashSet<string>? initialEnabledPluginIds = null,
        ScriptCommandHandlerFactory? scriptHandlerFactory = null,
        BuiltInCommandHandlerFactory? builtInHandlerFactory = null)
    {
        _stateRepository = stateRepository;
        _enabledIds = initialEnabledPluginIds ?? _stateRepository?.LoadEnabledPluginIds() ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _scriptHandlerFactory = scriptHandlerFactory;
        _builtInHandlerFactory = builtInHandlerFactory;
    }

    /// <inheritdoc />
    public async Task DiscoverAndInitializeAsync(IPluginHost host, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (string pluginId in _instantiatedPlugins.Keys.ToList())
            {
                await ShutdownInstantiatedPluginAsync(pluginId, CancellationToken.None).ConfigureAwait(false);
            }
            foreach (string pluginId in _registeredCommandIdsByPlugin.Keys.ToList())
            {
                UnregisterOwnedCommands(pluginId, host);
            }
            _registeredCommandIdsByPlugin.Clear();
            _entries.Clear();

            DiscoverEmbeddedBuiltInPlugins();

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

            if (Directory.Exists(host.PluginsUserDirectory))
            {
                foreach (var dir in Directory.GetDirectories(host.PluginsUserDirectory))
                {
                    if (IsIgnoredDirectory(dir)) continue;
                    LoadPluginDirectory(dir, isBuiltIn: false);
                }
            }

            foreach (var entry in _entries.Values)
            {
                if (_enabledIds.Contains(entry.Id) || entry.IsBuiltIn)
                {
                    await EnablePluginInternalAsync(entry.Id, host, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _gate.Release();
            RaiseRegistryChanged();
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
                    _widgetErrors.TryRemove(kvp.Key, out _);
                }
                catch (Exception ex)
                {
                    // A widget-surface failure is kept distinct from whole-plugin
                    // lifecycle state. The plugin stays Enabled so the registry, catalog and
                    // dispatcher remain consistent; the failure is recorded separately.
                    _widgetErrors[kvp.Key] = $"Widget retrieval failed: {ex.Message}";
                }
            }
        }
        return widgets;
    }

    /// <summary>
    /// Last widget-surface failure per plugin id. Empty when every enabled plugin's
    /// widget surface is healthy; registry state itself is unaffected.
    /// </summary>
    public IReadOnlyDictionary<string, string> WidgetErrors => _widgetErrors;

    /// <inheritdoc />
    public async Task EnablePluginAsync(string pluginId, IPluginHost host, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnablePluginInternalAsync(pluginId, host, ct).ConfigureAwait(false);

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
            RaiseRegistryChanged();
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

            UnregisterOwnedCommands(pluginId, host);
            await ShutdownInstantiatedPluginAsync(pluginId, CancellationToken.None).ConfigureAwait(false);

            if (_entries.TryGetValue(pluginId, out var entry))
            {
                _entries[pluginId] = entry with { State = PluginState.Disabled, ErrorMessage = null };
            }
        }
        finally
        {
            _gate.Release();
            RaiseRegistryChanged();
        }
    }

    private void RaiseRegistryChanged()
    {
        var subscribers = RegistryChanged;
        if (subscribers is null) return;
        foreach (EventHandler subscriber in subscribers.GetInvocationList())
        {
            try { subscriber(this, EventArgs.Empty); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PluginRegistry] RegistryChanged subscriber failed: {ex}");
            }
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
        // Only packages in a user-writable directory must present an external admission record;
        // built-in packages in the application install directory are exempt because that location
        // cannot be rewritten without elevation.
        var loadResult = JsonPluginLoader.LoadFromDirectory(dirPath, requireAdmissionRecord: !isBuiltIn);
        if (!loadResult.Success || loadResult.Manifest == null)
        {
            return;
        }

        var manifest = loadResult.Manifest;

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
        _entries[manifest.Id] = new PluginRegistryEntry(
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
            ErrorMessage: loadResult.ErrorMessage);
    }

    private void UnregisterOwnedCommands(string pluginId, IPluginHost host)
    {
        if (!_registeredCommandIdsByPlugin.Remove(pluginId, out var ownedIds))
        {
            return;
        }

        foreach (string commandId in ownedIds)
        {
            try { host.UnregisterCommand(commandId); } catch { }
        }
    }

    private async Task ShutdownInstantiatedPluginAsync(string pluginId, CancellationToken cancellationToken)
    {
        if (!_instantiatedPlugins.TryRemove(pluginId, out var existing))
        {
            return;
        }

        // A plugin that never returns from ShutdownAsync/DisposeAsync would hold _gate indefinitely
        // and freeze every subsequent discovery, enable and disable. Ask it to stop, then abandon
        // the operation when the bound elapses.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PluginShutdownTimeout);

        await BoundedLifecycleAsync(pluginId, "ShutdownAsync", () => existing.Plugin.ShutdownAsync(timeout.Token)).ConfigureAwait(false);
        await BoundedLifecycleAsync(pluginId, "DisposeAsync", () => existing.Plugin.DisposeAsync().AsTask()).ConfigureAwait(false);

        try
        {
            existing.LoadContext?.Unload();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PluginRegistry] Failed unloading plugin '{pluginId}': {ex.GetType().Name} - {ex.Message}");
        }
    }

    /// <summary>
    /// Awaits a plugin lifecycle operation for at most <see cref="PluginShutdownTimeout"/>. A plugin
    /// that ignores its cancellation token is abandoned instead of blocking the registry: the fault
    /// is logged and observed so a late completion cannot surface as an unobserved task exception.
    /// </summary>
    private static async Task BoundedLifecycleAsync(string pluginId, string operationName, Func<Task> operation)
    {
        Task? lifecycleTask = null;
        try
        {
            // Invocation itself can throw before returning a task. In-process synchronous
            // work cannot be forcibly timed out; the deadline bounds only the returned task.
            lifecycleTask = operation();
            await lifecycleTask.WaitAsync(PluginShutdownTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException) when (lifecycleTask is { IsCompleted: false })
        {
            System.Diagnostics.Debug.WriteLine($"[PluginRegistry] Plugin '{pluginId}' did not complete {operationName} within {(int)PluginShutdownTimeout.TotalSeconds}s and was abandoned to keep plugin lifecycle responsive.");
            _ = lifecycleTask.ContinueWith(
                task => System.Diagnostics.Debug.WriteLine($"[PluginRegistry] Abandoned {operationName} for plugin '{pluginId}' later faulted: {task.Exception?.InnerException?.GetType().Name} - {task.Exception?.InnerException?.Message}"),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PluginRegistry] Failed {operationName} for plugin '{pluginId}': {ex.GetType().Name} - {ex.Message}");
        }
    }

    private async Task<bool> InstantiateAssemblyPluginAsync(PluginRegistryEntry entry, PluginManifest manifest, CancellationToken cancellationToken)
    {
        var canonicalDir = Path.GetFullPath(entry.SourceDirectoryPath);
        var assemblyPath = Path.GetFullPath(Path.Combine(canonicalDir, manifest.AssemblyFileName!));
        var relativePath = Path.GetRelativePath(canonicalDir, assemblyPath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            await ShutdownInstantiatedPluginAsync(manifest.Id, CancellationToken.None).ConfigureAwait(false);
            _entries[manifest.Id] = entry with { State = PluginState.Error, ErrorMessage = "Security Violation: Assembly file path traverses outside plugin directory." };
            return false;
        }

        if (!string.Equals(manifest.TargetFramework, AssemblyPluginLoader.SupportedTargetFramework, StringComparison.OrdinalIgnoreCase))
        {
            await ShutdownInstantiatedPluginAsync(manifest.Id, CancellationToken.None).ConfigureAwait(false);
            _entries[manifest.Id] = entry with { State = PluginState.Error, ErrorMessage = $"Assembly plugins must declare targetFramework '{AssemblyPluginLoader.SupportedTargetFramework}'." };
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Bind the assembly to the bytes admitted at install time. A swapped assembly can keep
        // a still-valid manifest, so verifying the manifest alone is not enough to run compiled
        // plugin code. Absent a recorded digest (script plugins, direct test fixtures, or records
        // predating this binding) the pre-existing trust model applies unchanged.
        string? admittedAssemblyDigest = ReadAdmittedAssemblyDigest(entry.SourceDirectoryPath);
        if (admittedAssemblyDigest is not null)
        {
            string actualAssemblyDigest;
            try
            {
                using var assemblyStream = File.OpenRead(assemblyPath);
                actualAssemblyDigest = Convert.ToHexString(SHA256.HashData(assemblyStream)).ToLowerInvariant();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await ShutdownInstantiatedPluginAsync(manifest.Id, CancellationToken.None).ConfigureAwait(false);
                _entries[manifest.Id] = entry with { State = PluginState.Error, ErrorMessage = $"Plugin assembly could not be read for integrity verification: {ex.Message}" };
                return false;
            }

            if (!string.Equals(actualAssemblyDigest, admittedAssemblyDigest, StringComparison.OrdinalIgnoreCase))
            {
                await ShutdownInstantiatedPluginAsync(manifest.Id, CancellationToken.None).ConfigureAwait(false);
                _entries[manifest.Id] = entry with { State = PluginState.Error, ErrorMessage = "Security Violation: Plugin assembly no longer matches the bytes admitted at install time. Reinstall the plugin to rebind its compiled code." };
                return false;
            }
        }

        var asmResult = AssemblyPluginLoader.LoadPluginAssembly(assemblyPath, manifest.PluginClassName);
        if (asmResult.Success && asmResult.Plugin != null)
        {
            await ShutdownInstantiatedPluginAsync(manifest.Id, CancellationToken.None).ConfigureAwait(false);
            _instantiatedPlugins[manifest.Id] = (asmResult.Plugin, asmResult.LoadContext);
            return true;
        }

        await ShutdownInstantiatedPluginAsync(manifest.Id, CancellationToken.None).ConfigureAwait(false);
        _entries[manifest.Id] = entry with { State = PluginState.Error, ErrorMessage = asmResult.ErrorMessage ?? "Assembly load failed." };
        return false;
    }

    /// <summary>
    /// Returns the assembly digest bound by the external admission record, or null when no record
    /// exists or it does not bind the assembly bytes.
    /// </summary>
    private static string? ReadAdmittedAssemblyDigest(string? sourceDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectoryPath))
        {
            return null;
        }

        string admissionPath;
        try
        {
            admissionPath = PluginAdmissionTrustStore.GetRecordPath(sourceDirectoryPath);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (!File.Exists(admissionPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(admissionPath);
            var record = JsonSerializer.Deserialize<PluginAdmissionRecord>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return string.IsNullOrWhiteSpace(record?.AssemblySha256) ? null : record.AssemblySha256;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable admission record is reported by the manifest integrity path during
            // discovery; do not attempt assembly instantiation against trust evidence we could
            // not read.
            return null;
        }
    }

    private async Task EnablePluginInternalAsync(string pluginId, IPluginHost host, CancellationToken ct)
    {
        if (!_entries.TryGetValue(pluginId, out var entry))
        {
            return;
        }

        UnregisterOwnedCommands(pluginId, host);
        await ShutdownInstantiatedPluginAsync(pluginId, CancellationToken.None).ConfigureAwait(false);
        var commandsBeforeAttempt = host.RegisteredCommands
            .Select(command => command.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            PluginManifest? manifest = null;
            if (!string.IsNullOrEmpty(entry.SourceDirectoryPath))
            {
                // The admission gate applied at discovery must apply again here: an enable attempt
                // re-reads the manifest from disk and must not admit a package whose trust evidence
                // disappeared after discovery.
                var loadResult = JsonPluginLoader.LoadFromDirectory(
                    entry.SourceDirectoryPath, requireAdmissionRecord: !entry.IsBuiltIn);
                if (!loadResult.Success || loadResult.Manifest == null)
                {
                    throw new InvalidOperationException($"Manifest validation failed: {loadResult.ErrorMessage ?? "unknown error"}");
                }

                manifest = loadResult.Manifest;
                if (manifest.EntryType.Equals("Assembly", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(manifest.AssemblyFileName))
                {
                    if (!await InstantiateAssemblyPluginAsync(entry, manifest, ct).ConfigureAwait(false))
                    {
                        return;
                    }
                }
            }
            else if (entry.IsBuiltIn && _builtInManifests.TryGetValue(pluginId, out var builtInManifest))
            {
                manifest = builtInManifest;
            }

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
                await inst.Plugin.InitializeAsync(host, ct).ConfigureAwait(false);
            }

            var toolMap = manifest?.Tools?.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
            // Admission tiers are derived before registration so the entry's command list — which the
            // review cards are built from — cannot disagree with what the host registered.
            IReadOnlyList<CommandDefinition> effectiveCommands = EffectiveBuiltInCommands(entry, toolMap);
            if (!ReferenceEquals(effectiveCommands, entry.Commands))
            {
                _entries[pluginId] = entry with { Commands = effectiveCommands };
                entry = _entries[pluginId];
            }

            foreach (var cmd in effectiveCommands)
            {
                if (host.RegisteredCommands.Any(c => string.Equals(c.Id, cmd.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                ICommandHandler? handler = null;
                PluginToolDefinition? toolDef = null;
                toolMap?.TryGetValue(cmd.Id, out toolDef);

                if (toolDef != null &&
                    !string.IsNullOrWhiteSpace(toolDef.ScriptPath) &&
                    !string.IsNullOrWhiteSpace(entry.SourceDirectoryPath) &&
                    _scriptHandlerFactory != null)
                {
                    handler = _scriptHandlerFactory(
                        cmd.Id,
                        toolDef.ScriptPath,
                        entry.SourceDirectoryPath,
                        cmd.ReadOnly,
                        (IReadOnlyList<string>?)manifest?.DeclaredCapabilities ?? Array.Empty<string>());
                }
                else if (entry.IsBuiltIn)
                {
                    handler = _builtInHandlerFactory?.Invoke(cmd);
                    var targetCoreId = handler is null ? ResolveBuiltInCoreCommandId(cmd.Id, toolDef) : null;
                    if (handler is null && !string.IsNullOrWhiteSpace(targetCoreId))
                    {
                        try
                        {
                            handler = new BuiltInDelegatingHandler(cmd.Id, targetCoreId, host.CommandDispatcher);
                        }
                        catch
                        {
                            // If host has no dispatcher configured (e.g. mock host in unit tests), handler remains null.
                        }
                    }
                }

                if (!host.RegisterCommand(cmd, handler))
                {
                    throw new InvalidOperationException($"Command registration for '{cmd.Id}' was rejected by the host.");
                }
            }

            var addedCommandIds = host.RegisteredCommands
                .Select(command => command.Id)
                .Where(commandId => !commandsBeforeAttempt.Contains(commandId))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _registeredCommandIdsByPlugin[pluginId] = addedCommandIds;
            _entries[pluginId] = entry with { State = PluginState.Enabled, ErrorMessage = null };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await RollbackEnableAttemptAsync(pluginId, host, commandsBeforeAttempt).ConfigureAwait(false);
            _entries[pluginId] = entry with { State = PluginState.Disabled, ErrorMessage = "Initialization was cancelled." };
            throw;
        }
        catch (Exception ex)
        {
            await RollbackEnableAttemptAsync(pluginId, host, commandsBeforeAttempt).ConfigureAwait(false);
            _entries[pluginId] = entry with { State = PluginState.Error, ErrorMessage = $"Initialization failed: {ex.Message}" };
        }
    }

    private async Task RollbackEnableAttemptAsync(string pluginId, IPluginHost host, HashSet<string> commandsBeforeAttempt)
    {
        var addedCommandIds = host.RegisteredCommands
            .Select(command => command.Id)
            .Where(commandId => !commandsBeforeAttempt.Contains(commandId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (string commandId in addedCommandIds)
        {
            try { host.UnregisterCommand(commandId); } catch { }
        }

        _registeredCommandIdsByPlugin.Remove(pluginId);
        await ShutdownInstantiatedPluginAsync(pluginId, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Derives the command list a built-in plugin exposes for both registration and review cards. A
    /// built-in tool with no handler of its own re-dispatches to a core command, so it must be
    /// admitted at that command's tier when it is stricter than the plugin's own declaration: the
    /// card the user approves must not understate the operation that actually executes. Tools that
    /// have their own handler or a script keep their declared tier.
    /// </summary>
    private IReadOnlyList<CommandDefinition> EffectiveBuiltInCommands(
        PluginRegistryEntry entry,
        Dictionary<string, PluginToolDefinition>? toolMap)
    {
        if (!entry.IsBuiltIn || entry.Commands.Count == 0)
        {
            return entry.Commands;
        }

        var effective = new List<CommandDefinition>(entry.Commands.Count);
        bool changed = false;
        foreach (CommandDefinition command in entry.Commands)
        {
            PluginToolDefinition? toolDef = null;
            toolMap?.TryGetValue(command.Id, out toolDef);
            ICommandHandler? dedicatedHandler = _builtInHandlerFactory?.Invoke(command);
            string? delegatedCoreCommandId = dedicatedHandler is null
                ? ResolveBuiltInCoreCommandId(command.Id, toolDef)
                : null;

            CommandDefinition resolved = string.IsNullOrWhiteSpace(delegatedCoreCommandId)
                ? command
                : WithEffectiveDelegationTier(command, delegatedCoreCommandId!);
            changed |= !ReferenceEquals(resolved, command);
            effective.Add(resolved);
        }

        return changed ? effective : entry.Commands;
    }

    /// <summary>
    /// Derives the admission definition a built-in alias must be registered with: the stricter of
    /// the plugin command's declared tier and the delegated core command's own tier. Returns the
    /// original definition unchanged when the core command is unknown or no stricter than the alias.
    /// </summary>
    private static CommandDefinition WithEffectiveDelegationTier(CommandDefinition pluginCommand, string targetCoreCommandId)
    {
        CommandDefinition? target = WinCare.CommandCatalog.CommandCatalog.Find(targetCoreCommandId);
        if (target is null)
        {
            return pluginCommand;
        }

        RiskTier effective = (RiskTier)Math.Max((int)pluginCommand.RiskTier, (int)target.RiskTier);
        return effective == pluginCommand.RiskTier ? pluginCommand : pluginCommand with { ExplicitRiskTier = effective };
    }

    private static string? ResolveBuiltInCoreCommandId(string commandId, PluginToolDefinition? toolDef = null)
    {
        if (!string.IsNullOrWhiteSpace(toolDef?.AliasOf))
        {
            return toolDef.AliasOf;
        }

        return commandId.ToLowerInvariant() switch
        {
            "cleaner.system_temp" => "cleaner-disk-pressure",
            // The broad security collector is the supported native surface for Defender
            // state. Do not delegate to a synthetic command id that the core catalog does
            // not expose: that would register a plugin card which always fails at runtime.
            "security.defender_status" => "security",
            "cleaner.ai_models_huggingface" => "cleaner-disk-pressure",
            "cleaner.ai_models_ollama" => "cleaner-disk-pressure",
            "cleaner.ai_models_pytorch" => "cleaner-disk-pressure",
            "privacy.media_player" => "cleaner-disk-pressure",
            "privacy.vlc_history" => "cleaner-disk-pressure",
            "privacy.office_mru" => "cleaner-disk-pressure",
            "gpu.amd_ulps" => "peer-display-overrides",
            "gpu.nvidia_pstate" => "peer-display-overrides",
            "gpu.intel_async_flip" => "peer-display-overrides",
            "eventlog.diagnostic" => "deep-clean",
            "eventlog.powershell" => "deep-clean",
            "eventlog.bits" => "deep-clean",
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
                        _builtInManifests[manifest.Id] = manifest;
                        _entries[manifest.Id] = new PluginRegistryEntry(
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
                            ErrorMessage: loadResult.ErrorMessage);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PluginRegistry] Built-in plugin discovery failed: {ex.GetType().Name} - {ex.Message}");
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
                _entries[manifest.Id] = new PluginRegistryEntry(
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
                    ErrorMessage: loadResult.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PluginRegistry] Failed loading plugin JSON '{jsonFilePath}': {ex.GetType().Name} - {ex.Message}");
        }
    }
}

internal sealed class BuiltInDelegatingHandler : ICommandHandler
{
    private const int MaximumDelegatedPlans = 256;

    // Mirrors the dispatcher's own single-use review-plan window: a delegated receipt must not
    // outlive the plan it carries.
    private static readonly TimeSpan DelegatedPlanLifetime = TimeSpan.FromMinutes(15);

    private readonly string _commandId;
    private readonly string _targetCoreCommandId;
    private readonly ICommandDispatcher _dispatcher;
    private readonly ConcurrentDictionary<Guid, (ApprovedMutationPlan Plan, DateTimeOffset IssuedAtUtc)> _delegatedReviewPlans = new();

    public BuiltInDelegatingHandler(string commandId, string targetCoreCommandId, ICommandDispatcher dispatcher)
    {
        _commandId = commandId;
        _targetCoreCommandId = targetCoreCommandId;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public string CommandId => _commandId;

    public async Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        ApprovedMutationPlan? delegatedApproval = null;
        if (request.Apply)
        {
            DateTimeOffset issuedAtUtc = DateTimeOffset.UtcNow;
            if (_delegatedReviewPlans.TryRemove(request.CorrelationId, out var receipt))
            {
                delegatedApproval = receipt.Plan;
                issuedAtUtc = receipt.IssuedAtUtc;
            }

            if (delegatedApproval is null)
            {
                return CommandHandlerOutcome.Blocked(
                    "plugin.delegated_review_missing",
                    "The delegated core command no longer has the preview receipt created during this plugin command review. Preview the plugin command again before applying.");
            }

            if (DateTimeOffset.UtcNow - issuedAtUtc > DelegatedPlanLifetime)
            {
                return CommandHandlerOutcome.Blocked(
                    "plugin.delegated_review_expired",
                    "The preview receipt for this delegated plugin command expired. Preview the plugin command again before applying.");
            }
        }

        var mappedRequest = new CommandRequest(
            _targetCoreCommandId,
            request.Parameters,
            request.Apply,
            request.CorrelationId,
            delegatedApproval);

        var options = request.Apply ? new CommandExecutionOptions(ReviewApproved: true) : CommandExecutionOptions.Default;
        var result = await _dispatcher.ExecuteAsync(mappedRequest, options, cancellationToken).ConfigureAwait(false);

        if (!request.Apply && result.Status == CommandResultStatus.Succeeded && result.ReviewPlan is not null)
        {
            RecordDelegatedReviewPlan(request.CorrelationId, result.ReviewPlan);
        }

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

    /// <summary>
    /// Stores a delegated receipt with its issue time. A preview that is never applied would
    /// otherwise retain an entry for the process lifetime, so the store is bounded and expired
    /// receipts are reclaimed before a new one is admitted.
    /// </summary>
    private void RecordDelegatedReviewPlan(Guid correlationId, ApprovedMutationPlan plan)
    {
        if (_delegatedReviewPlans.Count >= MaximumDelegatedPlans)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var stale in _delegatedReviewPlans.Where(kvp => now - kvp.Value.IssuedAtUtc > DelegatedPlanLifetime).ToList())
            {
                _delegatedReviewPlans.TryRemove(stale.Key, out _);
            }

            if (_delegatedReviewPlans.Count >= MaximumDelegatedPlans)
            {
                var oldest = _delegatedReviewPlans.OrderBy(kvp => kvp.Value.IssuedAtUtc).FirstOrDefault();
                _delegatedReviewPlans.TryRemove(oldest.Key, out _);
            }
        }

        _delegatedReviewPlans[correlationId] = (plan, DateTimeOffset.UtcNow);
    }
}
