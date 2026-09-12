using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.Application.Plugins;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Tests;

public sealed class BuiltInPluginAliasTests : IDisposable
{
    private readonly string _pluginsDirectory = Path.Combine(Path.GetTempPath(), "wincare-builtins-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Defender_alias_delegates_to_the_supported_security_collector()
    {
        var executor = new RecordingExecutor();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService();

        await registry.DiscoverAndInitializeAsync(host);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("security.defender_status"),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("security", executor.LastDefinitionId);
    }

    [Fact]
    public async Task Temp_cleanup_alias_preserves_target_review_and_elevation_metadata()
    {
        var executor = new RecordingExecutor();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService();

        await registry.DiscoverAndInitializeAsync(host);

        CommandDefinition command = Assert.Single(host.RegisteredCommands, command => command.Id == "cleaner.system_temp");
        Assert.Equal(CommandRisk.Moderate, command.Risk);
        Assert.Equal(AdministratorAccess.Required, command.AdministratorAccess);
        Assert.Equal(RiskTier.Moderate, command.RiskTier);
        Assert.DoesNotContain(host.RegisteredCommands, command => command.Id == "cleaner.recycle_bin");
    }

    public void Dispose()
    {
        try { Directory.Delete(_pluginsDirectory, recursive: true); } catch (IOException) { }
    }

    private static CommandRequest Request(string id) =>
        new(id, JsonSerializer.SerializeToElement(new { }), Apply: false, Guid.NewGuid());

    private sealed class RecordingExecutor : ICommandOperationExecutor
    {
        public string? LastDefinitionId { get; private set; }

        public Task<CommandHandlerOutcome> ExecuteAsync(
            CommandDefinition definition,
            CommandRequest request,
            CancellationToken cancellationToken)
        {
            LastDefinitionId = definition.Id;
            return Task.FromResult(CommandHandlerOutcome.Succeeded("test.ok", "Completed."));
        }
    }
}
