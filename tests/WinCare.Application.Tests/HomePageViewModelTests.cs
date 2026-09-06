using WinCare.App.ViewModels.Pages;
using WinCare.Application.Commands;
using WinCare.Domain.Activity;
using WinCare.Domain.Commands;

namespace WinCare.Application.Tests;

public sealed class HomePageViewModelTests
{
    [Fact]
    public void Dashboard_shows_latest_activity_and_restores_empty_state()
    {
        var vm = new HomePageViewModel();
        var old = new ActivityRecord(Guid.NewGuid(), "old", "Old check", ActivityState.Completed,
            DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, "Old result", false);
        var recent = old with { Id = Guid.NewGuid(), Title = "Latest check", StartedAt = DateTimeOffset.UtcNow, Result = "Evidence collected" };
        vm.RefreshActivity([recent, old]);
        Assert.Equal("Latest check", vm.RecentActivityTitle);
        Assert.Contains("Evidence collected", vm.RecentActivitySummary);
        vm.RefreshActivity([]);
        Assert.Equal("No activity recorded", vm.RecentActivityTitle);
    }

    [Fact]
    public async Task Curated_quick_clean_executes_in_one_click_and_updates_status()
    {
        var cleanDef = new WinCare.CommandCatalog.Models.CommandDefinition(
            "cleaner-disk-pressure", "Disk Cleanup", "Clean temp files", "System care", "Clean up",
            WinCare.CommandCatalog.Models.CommandRisk.Low, false,
            WinCare.CommandCatalog.Models.AdministratorAccess.No,
            WinCare.CommandCatalog.Models.RestartExpectation.No,
            "test", WinCare.CommandCatalog.Models.MigrationStatus.Implemented,
            ["cleaner"], WinCare.Domain.Commands.RiskTier.Safe);

        var handler = new TestHandler("cleaner-disk-pressure", "Cleaned 1.2 GB");
        var dispatcher = new CommandDispatcher([cleanDef], [handler]);
        var vm = new HomePageViewModel(dispatcher);

        Assert.Equal("Ready", vm.CleanStatusText);
        Assert.False(vm.IsCleaning);

        await vm.QuickCleanCommand.ExecuteAsync(null);

        Assert.Equal("Clean Complete", vm.CleanStatusText);
        Assert.Equal("Cleaned 1.2 GB", vm.CleanDetailText);
        Assert.Equal("Clean Again", vm.CleanActionText);
        Assert.False(vm.IsCleaning);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Curated_startup_boost_and_network_refresh_execute_successfully()
    {
        var startupDef = new WinCare.CommandCatalog.Models.CommandDefinition(
            "startup", "Startup", "Analyze startup", "System care", "Performance",
            WinCare.CommandCatalog.Models.CommandRisk.ReadOnly, true,
            WinCare.CommandCatalog.Models.AdministratorAccess.No,
            WinCare.CommandCatalog.Models.RestartExpectation.No,
            "test", WinCare.CommandCatalog.Models.MigrationStatus.Implemented,
            ["startup"], WinCare.Domain.Commands.RiskTier.Safe);

        var networkDef = new WinCare.CommandCatalog.Models.CommandDefinition(
            "network", "Network", "Network summary", "System care", "Network",
            WinCare.CommandCatalog.Models.CommandRisk.ReadOnly, true,
            WinCare.CommandCatalog.Models.AdministratorAccess.No,
            WinCare.CommandCatalog.Models.RestartExpectation.No,
            "test", WinCare.CommandCatalog.Models.MigrationStatus.Implemented,
            ["network"], WinCare.Domain.Commands.RiskTier.Safe);

        var startupHandler = new TestHandler("startup", "12 startup items");
        var networkHandler = new TestHandler("network", "2 interfaces active");
        var dispatcher = new CommandDispatcher([startupDef, networkDef], [startupHandler, networkHandler]);
        var vm = new HomePageViewModel(dispatcher);

        await vm.StartupBoostCommand.ExecuteAsync(null);
        Assert.Equal("Audit Complete", vm.StartupStatusText);
        Assert.Equal("12 startup items", vm.StartupDetailText);
        Assert.Equal(1, startupHandler.CallCount);

        await vm.NetworkRefreshCommand.ExecuteAsync(null);
        Assert.Equal("Connected", vm.NetworkStatusText);
        Assert.Equal("2 interfaces active", vm.NetworkDetailText);
        Assert.Equal(1, networkHandler.CallCount);
    }

    [Fact]
    public async Task Progressive_disclosure_inspector_toggles_and_populates_telemetry()
    {
        var vm = new HomePageViewModel();
        Assert.False(vm.IsInspectorExpanded);
        Assert.Null(vm.TelemetryMetrics);

        await vm.ToggleInspectorCommand.ExecuteAsync(null);

        Assert.True(vm.IsInspectorExpanded);
        Assert.NotNull(vm.TelemetryMetrics);
        Assert.True(vm.TelemetryMetrics.LatencyMicroseconds >= 0);
        Assert.False(string.IsNullOrWhiteSpace(vm.TelemetryMetrics.TargetPathsSummary));

        await vm.ToggleInspectorCommand.ExecuteAsync(null);
        Assert.False(vm.IsInspectorExpanded);
    }


    private sealed class TestHandler(string id, string message) : ICommandHandler
    {
        public string CommandId { get; } = id;
        public int CallCount { get; private set; }

        public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(CommandHandlerOutcome.Succeeded(
                $"{CommandId}.ok",
                message,
                System.Text.Json.JsonSerializer.SerializeToElement(new { success = true })));
        }
    }
}
