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
            await service.DiscoverAndInitializeAsync(host);
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

        var stagingDir = Path.Combine(tempUserPluginsDir, ".staging", "backups", "old_plugin");
        Directory.CreateDirectory(stagingDir);
        File.WriteAllText(Path.Combine(stagingDir, "wincare-plugin.json"), """{"id": "staged.backup.plugin", "name": "Backup", "version": "0.1"}""");

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
        var service = new PluginRegistryService();

        try
        {
            await service.DiscoverAndInitializeAsync(host);
            var plugins = service.GetAllPlugins();
            Assert.Contains(plugins, p => p.Id == "com.wincare.disabled" && p.State == PluginState.Disabled);

            await service.EnablePluginAsync("com.wincare.disabled", host);
            Assert.Contains(service.GetAllPlugins(), p => p.Id == "com.wincare.disabled" && p.State == PluginState.Enabled);
            Assert.Contains(service.GetActivePluginCommands(), c => c.Id == "disabled.tool1");
            Assert.Contains(host.RegisteredCommands, c => c.Id == "disabled.tool1");

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
        File.WriteAllText(Path.Combine(pluginDir, "run.cmd"), "@echo off\r\necho E2E_PLUGIN_SUCCESS\r\nexit /b 0\r\n");

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
            scriptHandlerFactory: (cmdId, scriptPath, pDir, readOnly, capabilities) => new WinCare.Infrastructure.Plugins.PluginScriptCommandHandler(cmdId, scriptPath, pDir, declaredReadOnly: readOnly, declaredCapabilities: capabilities),
            initialEnabledPluginIds: new HashSet<string> { "com.wincare.e2e" });

        try
        {
            await service.DiscoverAndInitializeAsync(host);
            Assert.Contains(host.RegisteredCommands, c => c.Id == "e2e.tool1");
            var def = host.RegisteredCommands.First(c => c.Id == "e2e.tool1");
            Assert.Equal("E2E Tool", def.Title);

            var request = CommandRequest.Preview("e2e.tool1");
            var result = await dispatcher.ExecuteAsync(request, CommandExecutionOptions.Default, CancellationToken.None);
            Assert.Equal(CommandResultStatus.Succeeded, result.Status);
            Assert.Contains("E2E_PLUGIN_SUCCESS", result.Message);

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
        File.WriteAllText(Path.Combine(pluginDir, "tool.cmd"), "@echo off\r\necho OK\r\nexit /b 0\r\n");

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
            scriptHandlerFactory: (cmdId, scriptPath, pDir, readOnly, capabilities) => new WinCare.Infrastructure.Plugins.PluginScriptCommandHandler(cmdId, scriptPath, pDir, declaredReadOnly: readOnly, declaredCapabilities: capabilities),
            initialEnabledPluginIds: new HashSet<string> { "com.wincare.collision" });

        try
        {
            await service.DiscoverAndInitializeAsync(host);
            var plugin = Assert.Single(service.GetAllPlugins(), p => p.Id == "com.wincare.collision");
            Assert.Equal(PluginState.Error, plugin.State);
            Assert.NotNull(plugin.ErrorMessage);
            Assert.Contains("collision", plugin.ErrorMessage, StringComparison.OrdinalIgnoreCase);
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
            scriptHandlerFactory: (cmdId, scriptPath, pDir, readOnly, capabilities) => new WinCare.Infrastructure.Plugins.PluginScriptCommandHandler(cmdId, scriptPath, pDir, declaredReadOnly: readOnly, declaredCapabilities: capabilities),
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

    [Fact]
    public async Task PluginScriptCommandHandler_MutatingScript_DoesNotExecute_OnPreview()
    {
        var tempUserPluginsDir = Path.Combine(Path.GetTempPath(), "WinCareH1Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempUserPluginsDir);
        var pluginDir = Path.Combine(tempUserPluginsDir, "mutating-plugin");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "run.cmd"), "@echo off\r\necho MUTATION_RAN\r\nexit /b 0\r\n");

        var manifestJson = """
        {
          "id": "com.wincare.mutating",
          "name": "Mutating Plugin",
          "version": "1.0.0",
          "declaredCapabilities": ["filesystem.write", "process.spawn"],
          "tools": [
            {
              "id": "mutating.tool1",
              "title": "Mutating Tool",
              "area": "System care",
              "section": "Storage",
              "risk": "Moderate",
              "readOnly": false,
              "script": "run.cmd"
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(pluginDir, "wincare-plugin.json"), manifestJson);

        var dispatcher = new CommandDispatcher(Array.Empty<CommandDefinition>(), Array.Empty<ICommandHandler>());
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: tempUserPluginsDir);
        var service = new PluginRegistryService(
            scriptHandlerFactory: (cmdId, scriptPath, pDir, readOnly, capabilities) => new WinCare.Infrastructure.Plugins.PluginScriptCommandHandler(cmdId, scriptPath, pDir, declaredReadOnly: readOnly, declaredCapabilities: capabilities),
            initialEnabledPluginIds: new HashSet<string> { "com.wincare.mutating" });

        try
        {
            await service.DiscoverAndInitializeAsync(host);
            Assert.Contains(host.RegisteredCommands, c => c.Id == "mutating.tool1");
            CommandRequest previewRequest = CommandRequest.Preview("mutating.tool1");
            var preview = await dispatcher.ExecuteAsync(previewRequest, CommandExecutionOptions.Default, CancellationToken.None);
            Assert.Equal(CommandResultStatus.Succeeded, preview.Status);
            Assert.DoesNotContain("MUTATION_RAN", preview.Message);
            Assert.EndsWith(".preview", preview.Code);

            ApprovedMutationPlan approval = Assert.IsType<ApprovedMutationPlan>(preview.ReviewPlan);
            var apply = await dispatcher.ExecuteAsync(
                CommandRequest.Execute("mutating.tool1", previewRequest.Parameters, approval),
                new CommandExecutionOptions(ReviewApproved: true),
                CancellationToken.None);
            Assert.Equal(CommandResultStatus.Succeeded, apply.Status);
            Assert.Contains("MUTATION_RAN", apply.Message);
        }
        finally
        {
            Directory.Delete(tempUserPluginsDir, recursive: true);
        }
    }

    [Fact]
    public async Task PluginRegistryService_Invalid_TargetFramework_Rejects_And_Cleans_Up_State()
    {
        var tempUserPluginsDir = Path.Combine(Path.GetTempPath(), "WinCareTfmTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempUserPluginsDir);
        var pluginDir = Path.Combine(tempUserPluginsDir, "bad-tfm-plugin");
        Directory.CreateDirectory(pluginDir);

        var manifestJson = """
        {
          "id": "com.community.badtfm",
          "name": "Bad TFM Plugin",
          "version": "1.0.0",
          "author": "Community Developer",
          "category": "Utilities",
          "entryType": "Assembly",
          "targetFramework": "net7.0",
          "assemblyFileName": "PluginAssembly.dll",
          "pluginClassName": "Community.BadTfm.PluginEntryPoint",
          "tools": [
            {
              "id": "com.community.badtfm.execute",
              "title": "Bad TFM Tool",
              "area": "Utilities",
              "section": "General",
              "risk": "ReadOnly",
              "executorType": "Assembly"
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(pluginDir, "wincare-plugin.json"), manifestJson);

        var dispatcher = new CommandDispatcher(Array.Empty<CommandDefinition>(), Array.Empty<ICommandHandler>());
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: tempUserPluginsDir);
        var service = new PluginRegistryService(initialEnabledPluginIds: new HashSet<string> { "com.community.badtfm" });

        try
        {
            await service.DiscoverAndInitializeAsync(host);
            var plugin = Assert.Single(service.GetAllPlugins(), p => p.Id == "com.community.badtfm");
            Assert.Equal(PluginState.Error, plugin.State);
            Assert.Contains("targetFramework", plugin.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(host.RegisteredCommands, c => c.Id == "com.community.badtfm.execute");
        }
        finally
        {
            Directory.Delete(tempUserPluginsDir, recursive: true);
        }
    }

    [Fact]
    public async Task BuiltInMutatingAlias_AppliesThroughFreshApprovalPlan()
    {
        CommandDefinition diskPressureDef = new(
            Id: "cleaner-disk-pressure",
            Title: "Disk-pressure cleanup",
            Summary: "Assess free-space pressure and preview a bounded cleanup plan",
            Area: "System care",
            Section: "Clean up",
            Risk: CommandRisk.Moderate,
            ReadOnly: false,
            AdministratorAccess: AdministratorAccess.Required,
            Restart: RestartExpectation.No,
            LegacySource: "Cleaner.ps1",
            MigrationStatus: MigrationStatus.Implemented,
            Keywords: Array.Empty<string>());

        var dispatcher = new CommandDispatcher(new[] { diskPressureDef }, new ICommandHandler[] { new FakeSucceedingHandler("cleaner-disk-pressure") });
        var tempRoot = Path.Combine(Path.GetTempPath(), "WinCareAliasTest_" + Guid.NewGuid().ToString("N"));
        var userPluginsDir = Path.Combine(tempRoot, "UserPlugins");
        Directory.CreateDirectory(userPluginsDir);
        var host = new DefaultPluginHost(dispatcher, applicationRootPath: tempRoot, pluginsUserDirectory: userPluginsDir);
        var service = new PluginRegistryService();

        try
        {
            await service.DiscoverAndInitializeAsync(host);
            Assert.Contains(host.RegisteredCommands, c => c.Id == "cleaner.system_temp");

            using var paramsDoc = System.Text.Json.JsonDocument.Parse("""{}""");
            CommandRequest previewRequest = CommandRequest.Preview("cleaner.system_temp", paramsDoc.RootElement);
            CommandResult preview = await dispatcher.ExecuteAsync(previewRequest, CommandExecutionOptions.Default, CancellationToken.None);
            Assert.Equal(CommandResultStatus.Succeeded, preview.Status);
            ApprovedMutationPlan approval = Assert.IsType<ApprovedMutationPlan>(preview.ReviewPlan);

            var result = await dispatcher.ExecuteAsync(
                CommandRequest.Execute("cleaner.system_temp", paramsDoc.RootElement, approval),
                new CommandExecutionOptions(ReviewApproved: true),
                CancellationToken.None);

            Assert.Equal(CommandResultStatus.Succeeded, result.Status);
            Assert.Equal("cleaner-disk-pressure.applied", result.Code);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void JsonPluginLoader_RejectsManifestModifiedAfterAdmission()
    {
        var root = Path.Combine(Path.GetTempPath(), "WinCareIntegrityTest_" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "com.wincare.integrity");
        Directory.CreateDirectory(dir);

        try
        {
            const string manifestJson = """
            {
              "id": "com.wincare.integrity",
              "name": "Integrity Plugin",
              "version": "1.0.0",
              "tools": []
            }
            """;

            var manifestPath = Path.Combine(dir, "wincare-plugin.json");
            File.WriteAllText(manifestPath, manifestJson);
            var digest = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(manifestJson)))
                .ToLowerInvariant();

            var admissionPath = PluginAdmissionTrustStore.GetRecordPath(dir);
            Directory.CreateDirectory(Path.GetDirectoryName(admissionPath)!);
            File.WriteAllText(admissionPath, System.Text.Json.JsonSerializer.Serialize(new PluginAdmissionRecord
            {
                PluginId = "com.wincare.integrity",
                ManifestSha256 = digest
            }));

            var untampered = JsonPluginLoader.LoadFromDirectory(dir);
            Assert.True(untampered.Success, untampered.ErrorMessage);

            var tamperedJson = manifestJson.Replace("Integrity Plugin", "Tampered Plugin", StringComparison.Ordinal);
            File.WriteAllText(manifestPath, tamperedJson);
            var forgedLegacyDigest = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(tamperedJson)))
                .ToLowerInvariant();
            File.WriteAllText(Path.Combine(dir, ".wincare-manifest.sha256"), forgedLegacyDigest);

            var tampered = JsonPluginLoader.LoadFromDirectory(dir);
            Assert.False(tampered.Success);
            Assert.Contains("integrity", tampered.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeSucceedingHandler : ICommandHandler
    {
        public string CommandId { get; }
        public FakeSucceedingHandler(string commandId) => CommandId = commandId;

        public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(request.Apply
                ? CommandHandlerOutcome.Succeeded(CommandId + ".applied", "Apply succeeded")
                : CommandHandlerOutcome.Succeeded(CommandId + ".preview", "Preview succeeded"));
        }
    }
}