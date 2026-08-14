using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using WinCare.Application.Commands;
using WinCare.Application.Plugins;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Plugins;
using Xunit;

namespace WinCare.Infrastructure.Tests;

public class PluginSdkRoundTripTests
{
    [Fact]
    public async Task PluginSdk_Compiles_Packs_Installs_LoadsAssembly_And_ExecutesCommand_EndToEnd()
    {
        var tempRootDir = Path.Combine(Path.GetTempPath(), $"wincare_sdk_test_{Guid.NewGuid():N}");
        var stagingDir = Path.Combine(tempRootDir, "staging");
        var userPluginsDir = Path.Combine(tempRootDir, "installed_plugins");
        var packagePath = Path.Combine(tempRootDir, "com.community.goldensdk-1.0.0.wincare-plugin");

        Directory.CreateDirectory(stagingDir);
        Directory.CreateDirectory(userPluginsDir);

        try
        {
            // 1. Scaffold C# Source Code matching CLI Golden Template
            var sourceCode = @"
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.Application.Plugins;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace Community.GoldenSdkPlugin
{
    public class GoldenPluginEntryPoint : IWinCarePlugin
    {
        public string Id => ""com.community.goldensdk"";
        public string Name => ""Golden SDK Plugin"";
        public string Version => ""1.0.0"";
        public string Author => ""Community Developer"";
        public string Description => ""End-to-end compiled C# plugin"";

        public Task InitializeAsync(IPluginHost host, CancellationToken ct = default)
        {
            var cmd = new CommandDefinition(
                Id: ""com.community.goldensdk.execute"",
                Title: ""Golden Execution"",
                Summary: ""Executes compiled code"",
                Area: ""Utilities"",
                Section: ""General"",
                Risk: CommandRisk.ReadOnly,
                ReadOnly: true,
                AdministratorAccess: AdministratorAccess.No,
                Restart: RestartExpectation.No,
                LegacySource: ""plugin:com.community.goldensdk"",
                MigrationStatus: MigrationStatus.BehaviorVerified,
                Keywords: new[] { ""golden"", ""test"" }
            );

            host.RegisterCommand(cmd, new GoldenCommandHandler());
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken ct = default) => Task.CompletedTask;
        public IReadOnlyList<CommandDefinition> GetCommands() => Array.Empty<CommandDefinition>();
        public IReadOnlyList<IPluginWidget> GetWidgets() => Array.Empty<IPluginWidget>();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public class GoldenCommandHandler : ICommandHandler
    {
        public string CommandId => ""com.community.goldensdk.execute"";

        public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(CommandHandlerOutcome.Succeeded(""GOLDEN_SUCCESS"", ""Execution verified from dynamically loaded assembly""));
        }
    }
}
";

            // 2. Compile C# plugin using Roslyn CSharpCompilation
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
                MetadataReference.CreateFromFile(typeof(IWinCarePlugin).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(CommandDefinition).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(CommandRequest).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonElement).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ICommandHandler).Assembly.Location)
            };

            var compilation = CSharpCompilation.Create(
                "PluginAssembly",
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var assemblyFilePath = Path.Combine(stagingDir, "PluginAssembly.dll");
            var emitResult = compilation.Emit(assemblyFilePath);
            Assert.True(emitResult.Success, string.Join("\n", emitResult.Diagnostics.Select(d => d.GetMessage())));

            // 3. Create wincare-plugin.json manifest
            var manifestJson = """
            {
              "id": "com.community.goldensdk",
              "name": "Golden SDK Plugin",
              "version": "1.0.0",
              "author": "Community Developer",
              "description": "End-to-end compiled C# plugin",
              "category": "Utilities",
              "entryType": "Assembly",
              "assemblyFileName": "PluginAssembly.dll",
              "pluginClassName": "Community.GoldenSdkPlugin.GoldenPluginEntryPoint",
              "tools": [
                {
                  "id": "com.community.goldensdk.execute",
                  "title": "Golden Execution",
                  "summary": "Executes compiled code",
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
            File.WriteAllText(Path.Combine(stagingDir, "wincare-plugin.json"), manifestJson);

            // 4. Pack into .wincare-plugin package (ZIP)
            using (var zip = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(Path.Combine(stagingDir, "wincare-plugin.json"), "wincare-plugin.json");
                zip.CreateEntryFromFile(assemblyFilePath, "PluginAssembly.dll");
            }
            Assert.True(File.Exists(packagePath));

            // 5. Install package via PluginInstallerService
            var installer = new PluginInstallerService(pluginsBaseDirectory: userPluginsDir);
            var installedPluginDir = await installer.InstallPluginFromPackageAsync(new Uri(packagePath).AbsoluteUri, "com.community.goldensdk");
            Assert.True(Directory.Exists(installedPluginDir));
            Assert.True(File.Exists(Path.Combine(installedPluginDir, "PluginAssembly.dll")));

            // 6. Discover & Initialize via PluginRegistryService and DefaultPluginHost
            var dispatcher = new CommandDispatcher(Array.Empty<CommandDefinition>(), Array.Empty<ICommandHandler>());
            var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: userPluginsDir);
            var registryService = new PluginRegistryService(initialEnabledPluginIds: new HashSet<string> { "com.community.goldensdk" });

            await registryService.DiscoverAndInitializeAsync(host);

            // 7. Verify plugin state & registered command
            var plugin = Assert.Single(registryService.GetAllPlugins(), p => p.Id == "com.community.goldensdk");
            Assert.Equal(PluginState.Enabled, plugin.State);
            Assert.Null(plugin.ErrorMessage);
            Assert.Equal("Golden SDK Plugin", plugin.Name);

            var registeredCmd = Assert.Single(host.RegisteredCommands, c => c.Id == "com.community.goldensdk.execute");
            Assert.True(registeredCmd.ReadOnly);

            // 8. Execute dynamically registered command through CommandDispatcher
            var execOutcome = await dispatcher.ExecuteAsync(CommandRequest.Preview("com.community.goldensdk.execute"), CommandExecutionOptions.Default, CancellationToken.None);
            Assert.Equal(CommandResultStatus.Succeeded, execOutcome.Status);
            Assert.Equal("GOLDEN_SUCCESS", execOutcome.Code);
            Assert.Equal("Execution verified from dynamically loaded assembly", execOutcome.Message);
        }
        finally
        {
            if (Directory.Exists(tempRootDir))
            {
                Directory.Delete(tempRootDir, recursive: true);
            }
        }
    }
}
