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

    [Fact]
    public async Task Ai_model_cleaner_alias_delegates_to_cleaner_disk_pressure()
    {
        var executor = new RecordingExecutor();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService();

        await registry.DiscoverAndInitializeAsync(host);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("cleaner.ai_models_huggingface"),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("cleaner-disk-pressure", executor.LastDefinitionId);
    }

    [Fact]
    public async Task Ai_model_cleaner_lmstudio_alias_delegates_to_cleaner_disk_pressure()
    {
        var executor = new RecordingExecutor();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService();

        await registry.DiscoverAndInitializeAsync(host);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("cleaner.ai_models_lmstudio"),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("cleaner-disk-pressure", executor.LastDefinitionId);
    }


    [Fact]
    public async Task Media_privacy_alias_delegates_to_cleaner_disk_pressure()
    {
        var executor = new RecordingExecutor();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService();

        await registry.DiscoverAndInitializeAsync(host);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("privacy.vlc_history"),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("cleaner-disk-pressure", executor.LastDefinitionId);
    }

    [Fact]
    public async Task Gpu_tuner_alias_delegates_to_peer_display_overrides()
    {
        var executor = new RecordingExecutor();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService();

        await registry.DiscoverAndInitializeAsync(host);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("gpu.amd_ulps"),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("peer-display-overrides", executor.LastDefinitionId);
    }

    [Fact]
    public async Task Event_log_sanitizer_alias_delegates_to_deep_clean()
    {
        var executor = new RecordingExecutor();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService();

        await registry.DiscoverAndInitializeAsync(host);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("eventlog.powershell"),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("deep-clean", executor.LastDefinitionId);
    }

    [Fact]
    public async Task Adb_cleaner_alias_delegates_to_cleaner_disk_pressure()
    {
        var executor = new RecordingExecutor();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService();

        await registry.DiscoverAndInitializeAsync(host);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("cleaner.adb_wsa_cache"),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("cleaner-disk-pressure", executor.LastDefinitionId);
    }

    [Fact]
    public async Task Redis_cleaner_alias_delegates_to_cleaner_disk_pressure()
    {
        var executor = new RecordingExecutor();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService();

        await registry.DiscoverAndInitializeAsync(host);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("cleaner.redis_dump_aof"),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("cleaner-disk-pressure", executor.LastDefinitionId);
    }

    [Fact]
    public async Task Download_sanitizer_alias_delegates_to_cleaner_disk_pressure()
    {
        var executor = new RecordingExecutor();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService();

        await registry.DiscoverAndInitializeAsync(host);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("cleaner.downloads_incomplete_streams"),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("cleaner-disk-pressure", executor.LastDefinitionId);
    }

    [Fact]
    public async Task Desktop_widgets_alias_delegates_to_system()
    {
        var executor = new RecordingExecutor();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService();

        await registry.DiscoverAndInitializeAsync(host);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("widgets.cpu_ram_hud"),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("system", executor.LastDefinitionId);
    }

    [Fact]
    public async Task Hardware_tuner_alias_delegates_to_system()
    {
        var executor = new RecordingExecutor();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService();

        await registry.DiscoverAndInitializeAsync(host);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("hw.battery_health_probe"),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("system", executor.LastDefinitionId);
    }

    [Fact]
    public async Task Environment_sanitizer_alias_delegates_to_system()
    {
        var executor = new RecordingExecutor();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService();

        await registry.DiscoverAndInitializeAsync(host);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("env.path_integrity_audit"),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("system", executor.LastDefinitionId);
    }

    [Fact]
    public async Task Shell_extensibility_alias_delegates_to_system()
    {
        var executor = new RecordingExecutor();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService();

        await registry.DiscoverAndInitializeAsync(host);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("shell.context_menu_audit"),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("system", executor.LastDefinitionId);
    }

    [Fact]
    public async Task Storage_deduplication_alias_delegates_to_storage()
    {
        var executor = new RecordingExecutor();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService();

        await registry.DiscoverAndInitializeAsync(host);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("storage.duplicate_candidate_scan"),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("storage", executor.LastDefinitionId);
    }

    [Fact]
    public async Task Binary_diagnostics_alias_delegates_to_security()
    {
        var executor = new RecordingExecutor();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService();

        await registry.DiscoverAndInitializeAsync(host);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("diag.pe_binary_security_probe"),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("security", executor.LastDefinitionId);
    }

    [Fact]
    public async Task Window_manager_alias_delegates_to_system()
    {
        var executor = new RecordingExecutor();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService();

        await registry.DiscoverAndInitializeAsync(host);

        CommandResult result = await dispatcher.ExecuteAsync(
            Request("window.desktop_workspace_audit"),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("system", executor.LastDefinitionId);
    }

    [Fact]
    public async Task All_thirty_builtin_plugins_are_discovered_and_valid()
    {
        var executor = new RecordingExecutor();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService();

        await registry.DiscoverAndInitializeAsync(host);

        var plugins = registry.GetAllPlugins();
        Assert.True(plugins.Count >= 30);
        string[] expectedPluginIds =
        [
            "com.wincare.builtin.security",
            "com.wincare.builtin.system_care",
            "com.wincare.builtin.ai_model_cleaner",
            "com.wincare.builtin.media_privacy",
            "com.wincare.builtin.gpu_tuner",
            "com.wincare.builtin.event_log_sanitizer",
            "com.wincare.builtin.adb_cleaner",
            "com.wincare.builtin.redis_cleaner",
            "com.wincare.builtin.download_sanitizer",
            "com.wincare.builtin.desktop_widgets",
            "com.wincare.builtin.hardware_tuner",
            "com.wincare.builtin.environment_sanitizer",
            "com.wincare.builtin.shell_extensibility",
            "com.wincare.builtin.storage_deduplication",
            "com.wincare.builtin.binary_diagnostics",
            "com.wincare.builtin.window_manager",
            "com.wincare.builtin.power_scheme_governor",
            "com.wincare.builtin.bluetooth_monitor",       // actual JSON id
            "com.wincare.builtin.log_viewer",
            "com.wincare.builtin.theme_studio",
            "com.wincare.builtin.download_scheduler",
            "com.wincare.builtin.clipboard_security",
            "com.wincare.builtin.password_generator",
            "com.wincare.builtin.scoop_catalog",           // actual JSON id
            "com.wincare.builtin.dynamic_tiling",
            "com.wincare.builtin.segmented_downloader",
            "com.wincare.builtin.enterprise_utilities",
            "com.wincare.builtin.yaml_structure",
            "com.wincare.builtin.redis_inspector",
            "com.wincare.builtin.android_bridge"
        ];

        foreach (var id in expectedPluginIds)
        {
            Assert.Contains(plugins, p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Batch_ten_builtin_aliases_delegate_to_expected_native_catalogs()
    {
        var executor = new RecordingExecutor();
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService();

        await registry.DiscoverAndInitializeAsync(host);

        (string commandId, string expectedTargetId)[] batchTenAliases =
        [
            ("power.scheme_governor_audit",          "system"),
            ("peripherals.bluetooth_telemetry",      "system"),
            ("diagnostics.log_tailer_audit",         "system"),
            ("theme.palette_synthesizer_audit",      "system"),
            ("network.download_task_scheduler_audit","system"),
            ("security.clipboard_privacy_audit",     "security"),
            ("security.password_entropy_audit",      "security"),
            ("packages.dependency_graph_audit",      "system"),
            ("window.dynamic_tiling_audit",          "system"),
            ("network.segmented_download_audit",     "system"),
            ("security.enterprise_hosts_audit",      "security"),
            ("config.yaml_structure_audit",          "system"),
            ("diagnostics.redis_memory_audit",       "system"),
            ("devices.adb_storage_audit",            "system")
        ];

        foreach (var (commandId, expectedTargetId) in batchTenAliases)
        {
            CommandResult result = await dispatcher.ExecuteAsync(
                Request(commandId),
                CommandExecutionOptions.Default,
                CancellationToken.None);

            Assert.True(result.Status == CommandResultStatus.Succeeded, $"{commandId}: {result.Status} {result.Code} {result.Message}");
            Assert.Equal(expectedTargetId, executor.LastDefinitionId);
        }
    }

    [Fact]
    public async Task Dedicated_builtin_mutation_uses_its_handler_and_completes_review_apply_flow()
    {
        var executor = new RecordingExecutor();
        var direct = new RecordingDirectHandler("gpu.amd_ulps");
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);
        var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: _pluginsDirectory);
        var registry = new PluginRegistryService(
            builtInHandlerFactory: command => command.Id == direct.CommandId ? direct : null);

        await registry.DiscoverAndInitializeAsync(host);
        Guid correlationId = Guid.NewGuid();
        CommandResult preview = await dispatcher.ExecuteAsync(
            new CommandRequest(direct.CommandId, JsonSerializer.SerializeToElement(new { }), false, correlationId),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, preview.Status);
        Assert.NotNull(preview.ReviewPlan);
        CommandResult applied = await dispatcher.ExecuteAsync(
            new CommandRequest(direct.CommandId, JsonSerializer.SerializeToElement(new { }), true, correlationId, preview.ReviewPlan),
            new CommandExecutionOptions(ReviewApproved: true),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, applied.Status);
        Assert.True(direct.ApplyObserved);
        Assert.Null(executor.LastDefinitionId);
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

    private sealed class RecordingDirectHandler(string commandId) : ICommandHandler
    {
        public string CommandId { get; } = commandId;
        public bool ApplyObserved { get; private set; }

        public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            ApplyObserved |= request.Apply;
            return Task.FromResult(CommandHandlerOutcome.Succeeded("direct.ok", "Completed."));
        }
    }
}
