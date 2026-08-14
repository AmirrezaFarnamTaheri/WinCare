using WinCare.Application.Activity;
using WinCare.Application.Commands;
using WinCare.Application.Plugins;
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

    private AppRuntime()
    {
        Journal = new ActivityJournalService();
        NativeCore = new NativeCoreService();
        CommandExecutor = new WindowsCommandExecutor(NativeCore);
        Dispatcher = CommandRuntime.CreateDefault(CommandExecutor, NativeCore, Journal);
        PluginState = new PluginStateRepository();
        PluginHost = new DefaultPluginHost(Dispatcher);
        PluginRegistry = new PluginRegistryService(PluginState);
        CatalogService = new RemoteCatalogService();
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

    /// <summary>
    /// Gets the remote plugin catalog service instance.
    /// </summary>
    public IRemoteCatalogService CatalogService { get; }

    /// <summary>
    /// Gets the plugin installer service instance.
    /// </summary>
    public IPluginInstallerService InstallerService { get; }
}
