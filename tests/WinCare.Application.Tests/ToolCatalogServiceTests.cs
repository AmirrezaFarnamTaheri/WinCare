using System.Collections.Generic;
using System.Linq;
using WinCare.Application.Navigation;
using WinCare.Application.Plugins;
using WinCare.Application.Tools;
using WinCare.CommandCatalog.Models;
using Xunit;

namespace WinCare.Application.Tests;

public sealed class ToolCatalogServiceTests
{
    private readonly ToolCatalogService _service = new();

    [Fact]
    public void Empty_search_returns_all_commands()
    {
        Assert.Equal(259, _service.Search(string.Empty).Count);
    }

    [Theory]
    [InlineData("Windows Update", "wua-search")]
    [InlineData("quic", "quic-capability")]
    [InlineData("legacy-unsafe", "legacy-unsafe")]
    public void Search_matches_plain_language_and_command_ids(string query, string expectedId)
    {
        Assert.Contains(_service.Search(query), command => command.Id == expectedId);
    }

    [Fact]
    public void Read_only_filter_excludes_mutating_commands()
    {
        IReadOnlyList<CommandDefinition> commands = _service.Search(null, new ToolFilter(ReadOnly: true));

        Assert.NotEmpty(commands);
        Assert.All(commands, command => Assert.True(command.ReadOnly));
    }

    [Fact]
    public void Dynamic_plugin_commands_are_merged_and_cannot_override_core_commands()
    {
        var mockRegistry = new MockPluginRegistry(new List<CommandDefinition>
        {
            new("plugin.custom_scan", "Custom Scan", "Plugin tool", "Utilities", "General", CommandRisk.Low, true, AdministratorAccess.No, RestartExpectation.No, "plugin", MigrationStatus.BehaviorVerified, ["scan"]),
            new("wua-search", "Malicious Override", "Attempted overwrite", "Utilities", "General", CommandRisk.Critical, false, AdministratorAccess.Required, RestartExpectation.Required, "plugin", MigrationStatus.BehaviorVerified, ["fake"])
        });

        var dynamicService = new ToolCatalogService(mockRegistry);
        var allTools = dynamicService.All;

        // Custom plugin tool is present
        var custom = allTools.FirstOrDefault(c => c.Id == "plugin.custom_scan");
        Assert.NotNull(custom);
        Assert.Equal("Custom Scan", custom.Title);

        // Core tool is preserved and was NOT overwritten by the plugin
        var wua = allTools.FirstOrDefault(c => c.Id == "wua-search");
        Assert.NotNull(wua);
        Assert.NotEqual("Malicious Override", wua.Title);
    }

    [Fact]
    public void Catalog_cache_is_invalidated_when_RegistryChanged_fires()
    {
        var fakeRegistry = new MutableFakePluginRegistry();
        var service = new ToolCatalogService(fakeRegistry);

        // Initially no plugin commands
        Assert.DoesNotContain(service.All, c => c.Id == "dynamic.tool");
        Assert.DoesNotContain(service.Search("dynamic"), c => c.Id == "dynamic.tool");

        // Add a new command and fire event
        fakeRegistry.SetCommands([
            new CommandDefinition("dynamic.tool", "Dynamic Tool", "Dynamic Summary", "Utilities", "General", CommandRisk.Low, true, AdministratorAccess.No, RestartExpectation.No, "plugin", MigrationStatus.BehaviorVerified, ["dynamic"])
        ]);

        // Cache must be invalidated and return the new tool in All and Search
        Assert.Contains(service.All, c => c.Id == "dynamic.tool");
        Assert.Contains(service.Search("dynamic"), c => c.Id == "dynamic.tool");
    }

    [Fact]
    public void Navigation_catalog_matches_the_approved_primary_structure()
    {
        string[] labels = NavigationCatalog.Items.Select(item => item.Label).ToArray();

        Assert.Equal(
            ["Home", "Checkup", "System care", "Security", "Repair & recovery", "All tools", "Activity", "Settings"],
            labels);
        Assert.Equal(["Commands", "Categories", "Favorites", "Recent", "Presets"],
            NavigationCatalog.Items.Single(item => item.Id == "all-tools").Tabs);
    }

    private sealed class MutableFakePluginRegistry : IPluginRegistry
    {
        private IReadOnlyList<CommandDefinition> _commands = [];
        public event System.EventHandler? RegistryChanged;

        public void SetCommands(IReadOnlyList<CommandDefinition> commands)
        {
            _commands = commands;
            RegistryChanged?.Invoke(this, System.EventArgs.Empty);
        }

        public Task DiscoverAndInitializeAsync(IPluginHost host, CancellationToken ct = default) => Task.CompletedTask;
        public IReadOnlyList<PluginRegistryEntry> GetAllPlugins() => [];
        public IReadOnlyList<CommandDefinition> GetActivePluginCommands() => _commands;
        public IReadOnlyList<IPluginWidget> GetActivePluginWidgets() => [];
        public Task EnablePluginAsync(string pluginId, IPluginHost host, CancellationToken ct = default) => Task.CompletedTask;
        public Task DisablePluginAsync(string pluginId, IPluginHost host, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class MockPluginRegistry : IPluginRegistry
    {
        private readonly IReadOnlyList<CommandDefinition> _commands;
        public event System.EventHandler? RegistryChanged { add { } remove { } }

        public MockPluginRegistry(IReadOnlyList<CommandDefinition> commands)
        {
            _commands = commands;
        }

        public Task DiscoverAndInitializeAsync(IPluginHost host, CancellationToken ct = default) => Task.CompletedTask;
        public IReadOnlyList<PluginRegistryEntry> GetAllPlugins() => [];
        public IReadOnlyList<CommandDefinition> GetActivePluginCommands() => _commands;
        public IReadOnlyList<IPluginWidget> GetActivePluginWidgets() => [];
        public Task EnablePluginAsync(string pluginId, IPluginHost host, CancellationToken ct = default) => Task.CompletedTask;
        public Task DisablePluginAsync(string pluginId, IPluginHost host, CancellationToken ct = default) => Task.CompletedTask;
    }
}
