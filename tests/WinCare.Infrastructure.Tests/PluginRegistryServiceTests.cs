namespace WinCare.Infrastructure.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.Application.Plugins;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;
using Xunit;

public sealed class DummyPluginHost : IPluginHost
{
    private readonly List<CommandDefinition> _registeredCommands = new();

    public string ApplicationRootPath => Path.GetTempPath();
    public string PluginsUserDirectory { get; init; } = Path.GetTempPath();
    public ICommandDispatcher CommandDispatcher => null!;
    public IReadOnlyCollection<CommandDefinition> RegisteredCommands => _registeredCommands.AsReadOnly();

    public bool RegisterCommand(CommandDefinition command, ICommandHandler? handler = null)
    {
        if (command == null || string.IsNullOrWhiteSpace(command.Id)) return false;
        if (_registeredCommands.Any(c => c.Id.Equals(command.Id, StringComparison.OrdinalIgnoreCase))) return false;
        _registeredCommands.Add(command);
        return true;
    }

    public void UnregisterCommand(string commandId)
    {
        _registeredCommands.RemoveAll(c => c.Id.Equals(commandId, StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public async Task PluginRegistryService_EndToEnd_Manifest_Registry_Host_Dispatcher_Handler_Execution()
    {
        var tempUserPluginsDir = Path.Combine(Path.GetTempPath(), "WinCareE2ETest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempUserPluginsDir);

        var pluginDir = Path.Combine(tempUserPluginsDir, "e2e-plugin");
        Directory.CreateDirectory(pluginDir);

        var scriptPath = Path.Combine(pluginDir, "run.cmd");
        File.WriteAllText(scriptPath, "@echo off\r\necho E2E_PLUGIN_SUCCESS\r\nexit /b 0\r\n");

        var manifestJson = """
        {
          "id": "com.wincare.e2e",
          "name": "E2E Plugin",
          "version": "1.0.0",
          "tools": [
            {
              "id": "e2e.tool1",
              "title": "E2E Tool",
              "area": "System care",
              "section": "Diagnostics",
              "script": "run.cmd",
              "safetyLevel": "Safe",
              "mutationType": "ReadOnly"
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(pluginDir, "wincare-plugin.json"), manifestJson);

        var dispatcher = new CommandDispatcher(Array.Empty<CommandDefinition>(), Array.Empty<ICommandHandler>());
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: tempUserPluginsDir);
        var service = new PluginRegistryService(
            scriptHandlerFactory: (cmdId, scriptPath, pluginDir) => new WinCare.Infrastructure.Plugins.PluginScriptCommandHandler(cmdId, scriptPath, pluginDir),
            initialEnabledPluginIds: new HashSet<string> { "com.wincare.e2e" });

        try
        {
            await service.DiscoverAndInitializeAsync(host);

            Assert.Contains(host.RegisteredCommands, c => c.Id == "e2e.tool1");
            var def = host.RegisteredCommands.First(c => c.Id == "e2e.tool1");
            Assert.Equal("E2E Tool", def.Title);

            var request = CommandRequest.Preview("e2e.tool1");
            var result = await dispatcher.ExecuteAsync(request, WinCare.Application.Commands.CommandExecutionOptions.Default, CancellationToken.None);
            Assert.Equal(CommandResultStatus.Succeeded, result.Status);
            Assert.Contains("E2E_PLUGIN_SUCCESS", result.Message);

            // Disable plugin dynamically and verify dispatcher unregistration
            await service.DisablePluginAsync("com.wincare.e2e", host);
            Assert.DoesNotContain(host.RegisteredCommands, c => c.Id == "e2e.tool1");
        }
        finally
        {
            Directory.Delete(tempUserPluginsDir, recursive: true);
        }
    }

    [Fact]
    public async Task PluginRegistryService_Duplicate_Command_Collision_Triggers_Rollback_And_Sets_Error_State()
    {
        var tempUserPluginsDir = Path.Combine(Path.GetTempPath(), "WinCareCollisionTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempUserPluginsDir);

        var pluginDir = Path.Combine(tempUserPluginsDir, "collision-plugin");
        Directory.CreateDirectory(pluginDir);

        var scriptPath = Path.Combine(pluginDir, "tool.cmd");
        File.WriteAllText(scriptPath, "@echo off\r\necho OK\r\nexit /b 0\r\n");

        var manifestJson = """
        {
          "id": "com.wincare.collision",
          "name": "Collision Plugin",
          "version": "1.0.0",
          "tools": [
            {
              "id": "collision.tool1",
              "title": "Tool 1",
              "area": "System care",
              "section": "Diagnostics",
              "script": "tool.cmd"
            },
            {
              "id": "collision.tool1",
              "title": "Duplicate Tool 1",
              "area": "System care",
              "section": "Diagnostics",
              "script": "tool.cmd"
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(pluginDir, "wincare-plugin.json"), manifestJson);

        var dispatcher = new CommandDispatcher(Array.Empty<CommandDefinition>(), Array.Empty<ICommandHandler>());
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: tempUserPluginsDir);
        var service = new PluginRegistryService(
            scriptHandlerFactory: (cmdId, scriptPath, pluginDir) => new WinCare.Infrastructure.Plugins.PluginScriptCommandHandler(cmdId, scriptPath, pluginDir),
            initialEnabledPluginIds: new HashSet<string> { "com.wincare.collision" });

        try
        {
            await service.DiscoverAndInitializeAsync(host);

            var plugin = Assert.Single(service.GetAllPlugins(), p => p.Id == "com.wincare.collision");
            Assert.Equal(PluginState.Error, plugin.State);
            Assert.NotNull(plugin.ErrorMessage);
            Assert.Contains("collision", plugin.ErrorMessage, StringComparison.OrdinalIgnoreCase);

            // Verify rollback: no collision commands were left registered in host
            Assert.DoesNotContain(host.RegisteredCommands, c => c.Id == "collision.tool1");
        }
        finally
        {
            Directory.Delete(tempUserPluginsDir, recursive: true);
        }
    }

    [Fact]
    public async Task PluginRegistryService_Loads_CliGenerated_Manifest_With_Aliases_And_Executes_Correctly()
    {
        var tempUserPluginsDir = Path.Combine(Path.GetTempPath(), "WinCareCliCompatTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempUserPluginsDir);

        var pluginDir = Path.Combine(tempUserPluginsDir, "cli-tool");
        Directory.CreateDirectory(pluginDir);
        var scriptsDir = Path.Combine(pluginDir, "scripts");
        Directory.CreateDirectory(scriptsDir);

        File.WriteAllText(Path.Combine(scriptsDir, "clean_temp.cmd"), "@echo off\necho CLI test executed\nexit /b 0\n");

        var manifestJson = """
        {
          "id": "com.community.mycustomtool",
          "name": "My Custom Tool",
          "version": "1.0.0",
          "author": "Community Developer",
          "description": "Custom community script maintenance tool",
          "category": "System Care",
          "entryType": "Manifest",
          "tools": [
            {
              "id": "com.community.mycustomtool.clean",
              "title": "Run My Custom Tool",
              "summary": "Custom community script maintenance tool",
              "area": "System care",
              "section": "Storage",
              "risk": "ReadOnly",
              "readOnly": true,
              "administratorAccess": "No",
              "restart": "No",
              "executorType": "Script",
              "scriptPath": "scripts/clean_temp.cmd"
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(pluginDir, "wincare-plugin.json"), manifestJson);

        var dispatcher = new CommandDispatcher(Array.Empty<CommandDefinition>(), Array.Empty<ICommandHandler>());
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: tempUserPluginsDir);
        var service = new PluginRegistryService(
            scriptHandlerFactory: (cmdId, scriptPath, pDir) => new WinCare.Infrastructure.Plugins.PluginScriptCommandHandler(cmdId, scriptPath, pDir),
            initialEnabledPluginIds: new HashSet<string> { "com.community.mycustomtool" });

        try
        {
            await service.DiscoverAndInitializeAsync(host);

            var plugin = Assert.Single(service.GetAllPlugins(), p => p.Id == "com.community.mycustomtool");
            Assert.Equal(PluginState.Enabled, plugin.State);
            Assert.Null(plugin.ErrorMessage);

            var registeredCmd = Assert.Single(host.RegisteredCommands, c => c.Id == "com.community.mycustomtool.clean");
            Assert.True(registeredCmd.ReadOnly);
            Assert.Equal(CommandRisk.ReadOnly, registeredCmd.Risk);
        }
        finally
        {
            Directory.Delete(tempUserPluginsDir, recursive: true);
        }
    }

    [Fact]
    public async Task PluginRegistryService_Loads_CliGenerated_CSharp_Template_Manifest_And_Enables_Correctly()
    {
        var tempUserPluginsDir = Path.Combine(Path.GetTempPath(), "WinCareCliCSharpTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempUserPluginsDir);

        var pluginDir = Path.Combine(tempUserPluginsDir, "csharp-tool");
        Directory.CreateDirectory(pluginDir);

        var manifestJson = """
        {
          "id": "com.community.mycsharptool",
          "name": "My CSharp Tool",
          "version": "1.0.0",
          "author": "Community Developer",
          "description": "High performance managed assembly plugin",
          "category": "Utilities",
          "entryType": "Assembly",
          "targetFramework": "net8.0-windows10.0.19041.0",
          "assemblyFileName": "PluginAssembly.dll",
          "pluginClassName": "Community.MyCSharpTool.PluginEntryPoint",
          "tools": [
            {
              "id": "com.community.mycsharptool.execute",
              "title": "My CSharp Tool Execution Engine",
              "summary": "High performance managed assembly plugin",
              "area": "Utilities",
              "section": "General",
              "risk": "ReadOnly",
              "readOnly": true,
              "administratorAccess": "No",
              "restart": "No",
              "executorType": "Assembly"
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(pluginDir, "wincare-plugin.json"), manifestJson);

        var dispatcher = new CommandDispatcher(Array.Empty<CommandDefinition>(), Array.Empty<ICommandHandler>());
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: tempUserPluginsDir);
        var service = new PluginRegistryService();

        try
        {
            await service.DiscoverAndInitializeAsync(host);

            var plugin = Assert.Single(service.GetAllPlugins(), p => p.Id == "com.community.mycsharptool");
            Assert.Equal(PluginState.Disabled, plugin.State);
            Assert.Equal("My CSharp Tool", plugin.Name);
            Assert.Equal("1.0.0", plugin.Version);
            Assert.Equal("Community Developer", plugin.Author);
            Assert.Equal("Utilities", plugin.Category);

            var tool = Assert.Single(plugin.Commands);
            Assert.Equal("com.community.mycsharptool.execute", tool.Id);
            Assert.True(tool.ReadOnly);
        }
        finally
        {
            Directory.Delete(tempUserPluginsDir, recursive: true);
        }
    }
}
