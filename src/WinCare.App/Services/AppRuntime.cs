using WinCare.Application.Activity;
using WinCare.Application.Commands;
using WinCare.Application.Plugins;
using WinCare.Application.Tools;
using WinCare.Infrastructure.Commands;
using WinCare.Infrastructure.Native;
using WinCare.Infrastructure.Plugins;

namespace WinCare.App.Services;

/// <summary>
/// Process-lifetime composition root for shared native services.
/// </summary>
public sealed class AppRuntime
{
    private static readonly Lazy<AppRuntime> CurrentValue = new(() => new AppRuntime());
    private readonly object _pluginInitializationLock = new();
    private Task? _pluginInitializationTask;

    private AppRuntime()
    {
        Journal = new ActivityJournalService();
        NativeCore = new NativeCoreService();
        CommandExecutor = new WindowsCommandExecutor(NativeCore);
        Dispatcher = CommandRuntime.CreateDefault(CommandExecutor, NativeCore, Journal);
        PluginState = new PluginStateRepository();
        PluginHost = new DefaultPluginHost(Dispatcher);
        PluginRegistry = new PluginRegistryService(
            PluginState,
            scriptHandlerFactory: (cmdId, scriptPath, pluginDir, readOnly, capabilities) =>
                new PluginScriptCommandHandler(cmdId, scriptPath, pluginDir, new BoundedProcessRunner(), readOnly, capabilities)
        );
        CatalogService = new RemoteCatalogService();
        ToolCatalog = new ToolCatalogService(PluginRegistry);
        InstallerService = new PluginInstallerService();
    }

    /// <summary>
    /// Gets the singleton process runtime instance.
    /// </summary>
    public static AppRuntime Current => CurrentValue.Value;

    /// <summary>
    /// Gets the activity journal service instance.
    /// </summary>
    public ActivityJournalService Journal { get; }

    /// <summary>
    /// Gets the native core service instance.
    /// </summary>
    public NativeCoreService NativeCore { get; }

    /// <summary>
    /// Gets the Windows command executor instance.
    /// </summary>
    internal WindowsCommandExecutor CommandExecutor { get; }

    /// <summary>
    /// Gets the command dispatcher instance.
    /// </summary>
    public CommandDispatcher Dispatcher { get; }

    /// <summary>
    /// Gets the plugin state repository instance.
    /// </summary>
    public IPluginStateRepository PluginState { get; }

    /// <summary>
    /// Gets the shared plugin host instance.
    /// </summary>
    public IPluginHost PluginHost { get; }

    /// <summary>
    /// Gets the global plugin registry instance.
    /// </summary>
    public IPluginRegistry PluginRegistry { get; }

    /// <summary>Shared searchable catalog, subscribed once to the plugin registry.</summary>
    public ToolCatalogService ToolCatalog { get; }

    /// <summary>
    /// Gets the remote plugin catalog service instance.
    /// </summary>
    public IRemoteCatalogService CatalogService { get; }

    /// <summary>
    /// Gets the plugin installer service instance.
    /// </summary>
    public IPluginInstallerService InstallerService { get; }

    /// <summary>
    /// Discovers and initializes plugins asynchronously on application startup.
    /// </summary>
    public Task InitializePluginsAsync(CancellationToken ct = default)
    {
        lock (_pluginInitializationLock)
        {
            // Startup discovery is process-wide work. Sharing the task prevents parallel
            // registry initialization when a page opens while app startup is still running.
            // Inspect the stored task on entry: a synchronously cancelled operation can
            // complete before assignment, so clearing it from its own catch is insufficient.
            if (_pluginInitializationTask is null || _pluginInitializationTask.IsFaulted || _pluginInitializationTask.IsCanceled)
            {
                _pluginInitializationTask = PluginRegistry.DiscoverAndInitializeAsync(PluginHost, ct);
            }
            return _pluginInitializationTask;
        }
    }

}
