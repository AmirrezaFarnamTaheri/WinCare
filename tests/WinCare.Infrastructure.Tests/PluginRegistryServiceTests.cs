namespace WinCare.Infrastructure.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.Application.Plugins;
using WinCare.CommandCatalog.Models;
using Xunit;

public sealed class DummyPluginHost : IPluginHost
{
    public string ApplicationRootPath => Path.GetTempPath();
    public string PluginsUserDirectory { get; init; } = Path.GetTempPath();
    public ICommandDispatcher CommandDispatcher => null!;
    public List<CommandDefinition> RegisteredCommands { get; } = new();

    public void RegisterCommand(CommandDefinition command, ICommandHandler? handler = null)
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
    public async Task PluginRegistryService_Discovers_And_Enables_Plugins_And_Fires_RegistryChanged()
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

        int eventCount = 0;
        service.RegistryChanged += (s, e) => eventCount++;

        try
        {
            // Act
            await service.DiscoverAndInitializeAsync(host);

            // Assert
            Assert.True(eventCount >= 1);
            var plugins = service.GetAllPlugins();
            Assert.Contains(plugins, p => p.Id == "com.wincare.sample" && p.State == PluginState.Enabled);

            var activeCommands = service.GetActivePluginCommands();
            Assert.Contains(activeCommands, c => c.Id == "sample.tool1");
        }
        finally
        {
            Directory.Delete(tempUserPluginsDir, recursive: true);
        }
    }

    [Fact]
    public async Task PluginRegistryService_Ignores_Staging_And_Bak_Directories()
    {
        var tempUserPluginsDir = Path.Combine(Path.GetTempPath(), "WinCareUserPluginsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempUserPluginsDir);

        // Staging directory
        var stagingDir = Path.Combine(tempUserPluginsDir, ".staging", "backups", "old_plugin");
        Directory.CreateDirectory(stagingDir);
        File.WriteAllText(Path.Combine(stagingDir, "wincare-plugin.json"), """{"id": "staged.backup.plugin", "name": "Backup", "version": "0.1"}""");

        // .bak directory
        var bakDir = Path.Combine(tempUserPluginsDir, "legacy_plugin.bak");
        Directory.CreateDirectory(bakDir);
        File.WriteAllText(Path.Combine(bakDir, "wincare-plugin.json"), """{"id": "legacy.bak.plugin", "name": "Bak", "version": "0.1"}""");

        var host = new DummyPluginHost { PluginsUserDirectory = tempUserPluginsDir };
        var service = new PluginRegistryService();

        try
        {
            await service.DiscoverAndInitializeAsync(host);
            var allPlugins = service.GetAllPlugins();
            Assert.DoesNotContain(allPlugins, p => p.Id == "staged.backup.plugin");
            Assert.DoesNotContain(allPlugins, p => p.Id == "legacy.bak.plugin");
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

            // Assert: plugin is discovered inertly as Disabled
            var plugins = service.GetAllPlugins();
            Assert.Contains(plugins, p => p.Id == "com.wincare.disabled" && p.State == PluginState.Disabled);

            // Enable plugin dynamically
            await service.EnablePluginAsync("com.wincare.disabled", host);
            Assert.Contains(service.GetAllPlugins(), p => p.Id == "com.wincare.disabled" && p.State == PluginState.Enabled);
            Assert.Contains(service.GetActivePluginCommands(), c => c.Id == "disabled.tool1");
            Assert.Contains(host.RegisteredCommands, c => c.Id == "disabled.tool1");

            // Disable plugin dynamically
            await service.DisablePluginAsync("com.wincare.disabled", host);
            Assert.Contains(service.GetAllPlugins(), p => p.Id == "com.wincare.disabled" && p.State == PluginState.Disabled);
            Assert.DoesNotContain(service.GetActivePluginCommands(), c => c.Id == "disabled.tool1");
            Assert.DoesNotContain(host.RegisteredCommands, c => c.Id == "disabled.tool1");
        }
        finally
        {
            Directory.Delete(tempUserPluginsDir, recursive: true);
        }
    }
}
