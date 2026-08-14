namespace WinCare.Infrastructure.Tests;

using WinCare.Application.Commands;
using WinCare.Application.Plugins;
using WinCare.CommandCatalog.Models;

public sealed class DummyPluginHost : IPluginHost
{
    public string ApplicationRootPath => Path.GetTempPath();
    public string PluginsUserDirectory { get; init; } = Path.GetTempPath();
    public ICommandDispatcher CommandDispatcher => null!;
    public List<CommandDefinition> RegisteredCommands { get; } = new();

    public void RegisterCommand(CommandDefinition command)
    {
        RegisteredCommands.Add(command);
    }

    public void UnregisterCommand(string commandId)
    {
        RegisteredCommands.RemoveAll(c => c.Id.Equals(commandId, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class PluginRegistryServiceTests
{
    [Fact]
    public async Task PluginRegistryService_Discovers_And_Enables_Plugins()
    {
        // Arrange
        var tempUserPluginsDir = Path.Combine(Path.GetTempPath(), "WinCareUserPluginsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempUserPluginsDir);

        var pluginDir = Path.Combine(tempUserPluginsDir, "sample-plugin");
        Directory.CreateDirectory(pluginDir);

        var manifestJson = """
        {
          "id": "com.wincare.sample",
          "name": "Sample Plugin",
          "version": "1.0.0",
          "tools": [
            {
              "id": "sample.tool1",
              "title": "Sample Tool 1",
              "area": "System care",
              "section": "Storage"
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(pluginDir, "wincare-plugin.json"), manifestJson);

        var host = new DummyPluginHost { PluginsUserDirectory = tempUserPluginsDir };
        var enabledIds = new HashSet<string> { "com.wincare.sample" };
        var service = new PluginRegistryService(initialEnabledPluginIds: enabledIds);

        try
        {
            // Act
            await service.DiscoverAndInitializeAsync(host);

            // Assert
            var plugins = service.GetAllPlugins();
            Assert.Single(plugins);
            Assert.Equal("com.wincare.sample", plugins[0].Id);
            Assert.Equal(PluginState.Enabled, plugins[0].State);

            var activeCommands = service.GetActivePluginCommands();
            Assert.Single(activeCommands);
            Assert.Equal("sample.tool1", activeCommands[0].Id);
        }
        finally
        {
            Directory.Delete(tempUserPluginsDir, recursive: true);
        }
    }

    [Fact]
    public async Task PluginRegistryService_Discovers_Disabled_Plugins_Inertly()
    {
        // Arrange
        var tempUserPluginsDir = Path.Combine(Path.GetTempPath(), "WinCareUserPluginsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempUserPluginsDir);

        var pluginDir = Path.Combine(tempUserPluginsDir, "disabled-plugin");
        Directory.CreateDirectory(pluginDir);

        var manifestJson = """
        {
          "id": "com.wincare.disabled",
          "name": "Disabled Plugin",
          "version": "1.0.0",
          "tools": [
            {
              "id": "disabled.tool1",
              "title": "Disabled Tool 1",
              "area": "System care",
              "section": "Storage"
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(pluginDir, "wincare-plugin.json"), manifestJson);

        var host = new DummyPluginHost { PluginsUserDirectory = tempUserPluginsDir };
        var service = new PluginRegistryService(); // Default: nothing enabled

        try
        {
            // Act
            await service.DiscoverAndInitializeAsync(host);

            // Assert: plugin is discovered inertly as Disabled, 0 active commands
            var plugins = service.GetAllPlugins();
            Assert.Single(plugins);
            Assert.Equal("com.wincare.disabled", plugins[0].Id);
            Assert.Equal(PluginState.Disabled, plugins[0].State);
            Assert.Empty(service.GetActivePluginCommands());
            Assert.Empty(host.RegisteredCommands);

            // Enable plugin dynamically
            await service.EnablePluginAsync("com.wincare.disabled", host);
            Assert.Equal(PluginState.Enabled, service.GetAllPlugins()[0].State);
            Assert.Single(service.GetActivePluginCommands());
            Assert.Single(host.RegisteredCommands);

            // Disable plugin dynamically
            await service.DisablePluginAsync("com.wincare.disabled", host);
            Assert.Equal(PluginState.Disabled, service.GetAllPlugins()[0].State);
            Assert.Empty(service.GetActivePluginCommands());
            Assert.Empty(host.RegisteredCommands);
        }
        finally
        {
            Directory.Delete(tempUserPluginsDir, recursive: true);
        }
    }
}
