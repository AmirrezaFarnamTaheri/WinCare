using System.Collections.Concurrent;
using System.Reflection;
using WinCare.Application.Activity;
using WinCare.Application.Plugins;
using WinCare.Application.Tools;
using WinCare.CommandCatalog.Models;

namespace WinCare.Application.Tests;

public sealed class ApplicationReliabilityRefinementTests
{
    [Fact]
    public async Task Journal_notifies_later_subscribers_after_a_listener_throws()
    {
        var journal = new ActivityJournalService(TemporaryPath("journal.json"));
        int notifications = 0;
        journal.Changed += (_, _) => throw new InvalidOperationException("Inert listener failure");
        journal.Changed += (_, _) => notifications++;
        var record = journal.Begin("sample", "Sample");
        journal.Complete(record.Id, "Done");
        await journal.FlushAsync();
        Assert.Equal(2, notifications);
    }

    [Fact]
    public async Task Registry_and_catalog_isolate_each_subscriber()
    {
        var registry = new PluginRegistryService();
        registry.RegistryChanged += (_, _) => throw new InvalidOperationException("Inert registry listener");
        var catalog = new ToolCatalogService(registry);
        catalog.CatalogChanged += (_, _) => throw new InvalidOperationException("Inert catalog listener");
        int catalogNotifications = 0;
        int registryNotifications = 0;
        catalog.CatalogChanged += (_, _) => catalogNotifications++;
        registry.RegistryChanged += (_, _) => registryNotifications++;
        var directory = TemporaryPath("plugins");
        var host = new DefaultPluginHost(applicationRootPath: directory, pluginsUserDirectory: directory);
        // Unknown plugin operations exercise all notification sites without loading plugin code.
        await registry.EnablePluginAsync("absent", host);
        await registry.DisablePluginAsync("absent", host);
        Assert.Equal(2, catalogNotifications);
        Assert.Equal(2, registryNotifications);
    }

    [Fact]
    public async Task Concurrent_journal_updates_flush_the_latest_memory_snapshot()
    {
        string path = TemporaryPath("journal.json");
        var journal = new ActivityJournalService(path);
        var record = journal.Begin("sample", "Sample");
        await Task.WhenAll(Enumerable.Range(0, 64).Select(i => Task.Run(() => journal.Complete(record.Id, $"Result {i}"))));
        await journal.FlushAsync();
        var expected = Assert.Single(journal.GetAll());
        var actual = Assert.Single(new ActivityJournalService(path).GetAll());
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Synchronous_shutdown_failure_still_attempts_disposal()
    {
        var registry = new PluginRegistryService();
        var plugin = new ThrowingPlugin();
        var field = typeof(PluginRegistryService).GetField("_instantiatedPlugins", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var instances = (ConcurrentDictionary<string, (IWinCarePlugin Plugin, PluginLoadContext? LoadContext)>)field.GetValue(registry)!;
        instances[plugin.Id] = (plugin, null);
        var directory = TemporaryPath("plugins");
        var host = new DefaultPluginHost(applicationRootPath: directory, pluginsUserDirectory: directory);
        await registry.DisablePluginAsync(plugin.Id, host);
        Assert.True(plugin.DisposalAttempted);
        Assert.Empty(instances);
        await registry.DisablePluginAsync(plugin.Id, host);
    }

    private static string TemporaryPath(string name) =>
        Path.Combine(Path.GetTempPath(), "WinCare-reliability-" + Guid.NewGuid().ToString("N"), name);

    private sealed class ThrowingPlugin : IWinCarePlugin
    {
        public string Id => "sample";
        public string Name => "Sample";
        public string Version => "1.0.0";
        public string Author => "Test";
        public string Description => "Inert lifecycle fixture";
        public bool DisposalAttempted { get; private set; }
        public Task InitializeAsync(IPluginHost host, CancellationToken ct = default) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken ct = default) => throw new InvalidOperationException("Synchronous shutdown");
        public ValueTask DisposeAsync()
        {
            DisposalAttempted = true;
            throw new InvalidOperationException("Synchronous disposal");
        }
        public IReadOnlyList<CommandDefinition> GetCommands() => [];
        public IReadOnlyList<IPluginWidget> GetWidgets() => [];
    }
}
