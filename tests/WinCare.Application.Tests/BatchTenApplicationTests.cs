using WinCare.Application.Configuration;
using WinCare.Application.Devices;
using WinCare.Application.Diagnostics;
using WinCare.Application.Downloads;
using WinCare.Application.Packages;
using WinCare.Application.QuickActions;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Theme;
using Xunit;

namespace WinCare.Application.Tests;

/// <summary>
/// Unit and regression tests for Batch 10 application domain components:
/// PackageDependencyGraph, QuickActionPaletteService, ThemePaletteEngine,
/// DownloadTaskScheduler, AndroidDeviceBridgeService, RedisDiagnosticService,
/// and StructuredDocumentTreeService.
/// </summary>
public sealed class BatchTenApplicationTests
{
    [Fact]
    public void SemVersion_parses_and_compares_semver()
    {
        var v1 = SemVersion.Parse("1.2.3");
        Assert.Equal(1, v1.Major);
        Assert.Equal(2, v1.Minor);
        Assert.Equal(3, v1.Patch);
        Assert.Empty(v1.PreRelease);

        var v2 = SemVersion.Parse("2.0.0-rc.1+sha.abcdef");
        Assert.Equal(2, v2.Major);
        Assert.Equal(0, v2.Minor);
        Assert.Equal(0, v2.Patch);
        Assert.Equal("rc.1", v2.PreRelease);
        Assert.Equal("sha.abcdef", v2.BuildMetadata);

        Assert.True(v1.CompareTo(v2) < 0);

        var v3 = SemVersion.Parse("1.2.4");
        Assert.True(v1.CompareTo(v3) < 0);

        // Pre-release versions have lower precedence than normal version
        var vNormal = SemVersion.Parse("1.0.0");
        var vPre = SemVersion.Parse("1.0.0-alpha");
        Assert.True(vPre.CompareTo(vNormal) < 0);
    }

    [Fact]
    public void PackageDependencyGraph_sorts_topologically_and_detects_cycles()
    {
        var graph = new PackageDependencyGraph();

        // Dependencies:
        // CoreLib -> none
        // NetworkLib -> CoreLib
        // DatabaseLib -> CoreLib
        // AppHost -> NetworkLib, DatabaseLib
        graph.AddPackage("CoreLib");
        graph.AddDependency("NetworkLib", "CoreLib");
        graph.AddDependency("DatabaseLib", "CoreLib");
        graph.AddDependency("AppHost", "NetworkLib");
        graph.AddDependency("AppHost", "DatabaseLib");

        var installOrder = graph.ResolveOrder();
        Assert.Equal(4, installOrder.Count);

        // CoreLib must be installed before NetworkLib and DatabaseLib
        int coreIndex = -1, netIndex = -1, dbIndex = -1, appIndex = -1;
        for (int i = 0; i < installOrder.Count; i++)
        {
            if (installOrder[i].Equals("CoreLib", StringComparison.OrdinalIgnoreCase)) coreIndex = i;
            if (installOrder[i].Equals("NetworkLib", StringComparison.OrdinalIgnoreCase)) netIndex = i;
            if (installOrder[i].Equals("DatabaseLib", StringComparison.OrdinalIgnoreCase)) dbIndex = i;
            if (installOrder[i].Equals("AppHost", StringComparison.OrdinalIgnoreCase)) appIndex = i;
        }

        Assert.True(coreIndex < netIndex);
        Assert.True(coreIndex < dbIndex);
        Assert.True(netIndex < appIndex);
        Assert.True(dbIndex < appIndex);

        // Cycle detection test
        var cyclicGraph = new PackageDependencyGraph();
        cyclicGraph.AddDependency("A", "B");
        cyclicGraph.AddDependency("B", "C");
        cyclicGraph.AddDependency("C", "A");

        Assert.Throws<InvalidOperationException>(() => cyclicGraph.ResolveOrder());
    }

    [Fact]
    public void QuickActionPaletteService_ranks_fuzzy_matches_and_tokenizes()
    {
        // Test query tokenization with quotes
        var tokens = QuickActionPaletteService.TokenizeQuery("clean --target \"C:\\Program Files\\Temp\" --force");
        Assert.Equal(4, tokens.Count);
        Assert.Equal("clean", tokens[0]);
        Assert.Equal("--target", tokens[1]);
        Assert.Equal("C:\\Program Files\\Temp", tokens[2]);
        Assert.Equal("--force", tokens[3]);

        var cmd1 = new CommandDefinition(
            "cleaner.disk",
            "Disk Space Cleaner",
            "Frees up drive capacity by clearing system temporary files",
            "Maintenance",
            "Cleanup",
            CommandRisk.Low,
            false,
            AdministratorAccess.No,
            RestartExpectation.No,
            "test",
            MigrationStatus.BehaviorVerified,
            ["disk", "temp", "clean", "storage"]);

        var cmd2 = new CommandDefinition(
            "network.dns",
            "Flush DNS Resolver Cache",
            "Flushes and resets the contents of the DNS client resolver cache",
            "Network",
            "DNS",
            CommandRisk.ReadOnly,
            true,
            AdministratorAccess.No,
            RestartExpectation.No,
            "test",
            MigrationStatus.BehaviorVerified,
            ["dns", "network", "cache", "resolver"]);

        var cmd3 = new CommandDefinition(
            "security.defender",
            "Scan Malware Signatures",
            "Initiates quick antivirus probe on critical memory regions",
            "Security",
            "Antivirus",
            CommandRisk.ReadOnly,
            true,
            AdministratorAccess.No,
            RestartExpectation.No,
            "test",
            MigrationStatus.BehaviorVerified,
            ["security", "antivirus", "malware"]);

        CommandDefinition[] catalog = [cmd1, cmd2, cmd3];

        // Search with arguments: primary term "dns" + argument "--flush"
        var dnsResults = QuickActionPaletteService.SearchCommands("dns --flush", catalog);
        Assert.NotEmpty(dnsResults);
        Assert.Equal("network.dns", dnsResults[0].Command.Id);
        Assert.Equal(["--flush"], dnsResults[0].ExtractedArguments);

        // Keyword match for "clean"
        var cleanResults = QuickActionPaletteService.SearchCommands("clean", catalog);
        Assert.NotEmpty(cleanResults);
        Assert.Equal("cleaner.disk", cleanResults[0].Command.Id);

        // Empty search returns empty list
        var emptyResults = QuickActionPaletteService.SearchCommands("   ", catalog);
        Assert.Empty(emptyResults);
    }

    [Fact]
    public void ThemePaletteEngine_synthesizes_md3_and_presets()
    {
        // Luminance calculation tests
        double whiteLum = ThemePaletteEngine.CalculateLuminance("#FFFFFF");
        Assert.True(whiteLum > 0.99);

        double blackLum = ThemePaletteEngine.CalculateLuminance("#000000");
        Assert.Equal(0.0, blackLum);

        Assert.True(ThemePaletteEngine.IsLightColor("#FFFFFF"));
        Assert.False(ThemePaletteEngine.IsLightColor("#000000"));

        // Enumerate presets: 8 distinct theme families
        var presets = ThemePaletteEngine.GetPresetPalettes();
        Assert.True(presets.Count >= 8);
        Assert.Contains(presets, p => p.Family == "Catppuccin");
        Assert.Contains(presets, p => p.Family == "Dracula");
        Assert.Contains(presets, p => p.Family == "Nord");
        Assert.Contains(presets, p => p.Family == "Everforest");

        // Inspect Dracula (Dark)
        var dracula = Assert.Single(presets, p => p.Id == "dracula");
        Assert.True(dracula.IsDark);
        Assert.Equal("Dracula", dracula.Family);
        Assert.True(dracula.ColorTokens.ContainsKey("Primary"));
        Assert.True(dracula.ColorTokens.ContainsKey("Background"));
        Assert.True(dracula.ColorTokens.ContainsKey("Surface"));
    }

    [Fact]
    public void DownloadTaskScheduler_schedules_and_throttles_tasks()
    {
        using var scheduler = new DownloadTaskScheduler(maxConcurrentDownloads: 3);

        string taskId = scheduler.EnqueueTask(
            "https://downloads.example.org/packages/tool.zip",
            Path.Combine(Path.GetTempPath(), "tool.zip"),
            priority: 5);

        Assert.False(string.IsNullOrWhiteSpace(taskId));

        var task = scheduler.GetTask(taskId);
        Assert.NotNull(task);
        Assert.Equal(taskId, task.TaskId);
        Assert.Equal(DownloadStatus.Pending, task.Status);

        // Cancel the task
        bool canceled = scheduler.CancelTask(taskId);
        Assert.True(canceled);

        var canceledTask = scheduler.GetTask(taskId);
        Assert.NotNull(canceledTask);
        Assert.Equal(DownloadStatus.Cancelled, canceledTask.Status);
    }

    [Fact]
    public void AndroidDeviceBridgeService_parses_adb_and_df()
    {
        string rawAdbDevices =
            "List of devices attached\n" +
            "emulator-5554          device product:sdk_gphone64_x86_64 model:sdk_gphone64_x86_64 device:emulator64_x86_64 transport_id:1\n" +
            "R5CT30ABCDE            unauthorized transport_id:2\n";

        var devices = AndroidDeviceBridgeService.ParseAdbDevicesOutput(rawAdbDevices);
        Assert.Equal(2, devices.Count);

        var dev1 = devices[0];
        Assert.Equal("emulator-5554", dev1.Serial);
        Assert.Equal("device", dev1.State);
        Assert.Equal("sdk_gphone64_x86_64", dev1.Model);
        Assert.True(dev1.IsEmulator);

        var dev2 = devices[1];
        Assert.Equal("R5CT30ABCDE", dev2.Serial);
        Assert.Equal("unauthorized", dev2.State);
        Assert.False(dev2.IsEmulator);

        string rawDf =
            "Filesystem     1K-blocks    Used Available Use% Mounted on\n" +
            "/dev/block/dm-0  3956408 2145600   1794424  55% /system\n" +
            "/dev/block/dm-1  8192000 4096000   4096000  50% /data\n";

        var partitions = AndroidDeviceBridgeService.ParseDfOutput(rawDf);
        Assert.Equal(2, partitions.Count);
        Assert.Equal("/system", partitions[0].MountPoint);
        Assert.Equal(54.2, partitions[0].UsedPercentage);
        Assert.Equal("/data", partitions[1].MountPoint);
        Assert.Equal(50.0, partitions[1].UsedPercentage);
    }

    [Fact]
    public void RedisDiagnosticService_parses_redis_info_memory()
    {
        string rawInfo =
            "# Memory\r\n" +
            "used_memory:1048576\r\n" +
            "used_memory_rss:2097152\r\n" +
            "used_memory_peak:4194304\r\n" +
            "mem_fragmentation_ratio:2.00\r\n" +
            "evicted_keys:15\r\n" +
            "expired_keys:42\r\n" +
            "connected_clients:5\r\n" +
            "uptime_in_seconds:3600\r\n";

        var metrics = RedisDiagnosticService.ParseInfoOutput(rawInfo);

        Assert.Equal(1048576, metrics.UsedMemoryBytes);
        Assert.Equal(2097152, metrics.UsedMemoryRssBytes);
        Assert.Equal(4194304, metrics.UsedMemoryPeakBytes);
        Assert.InRange(metrics.MemoryFragmentationRatio, 1.99, 2.01);
        Assert.Equal(15, metrics.EvictedKeys);
        Assert.Equal(42, metrics.ExpiredKeys);
        Assert.Equal(5, metrics.ConnectedClients);
        Assert.Equal(3600, metrics.UptimeInSeconds);
    }

    [Fact]
    public void StructuredDocumentTreeService_parses_yaml_and_queries_paths()
    {
        string yaml =
            "server:\n" +
            "  host: 127.0.0.1\n" +
            "  port: 8080\n" +
            "  security:\n" +
            "    ssl: true\n" +
            "logging:\n" +
            "  level: debug\n";

        var roots = StructuredDocumentTreeService.ParseYamlStructure(yaml);
        Assert.NotEmpty(roots);

        var hostVal = StructuredDocumentTreeService.QueryPath(roots, "server.host");
        Assert.Equal("127.0.0.1", hostVal);

        var sslVal = StructuredDocumentTreeService.QueryPath(roots, "server.security.ssl");
        Assert.Equal("true", sslVal);

        var logLevel = StructuredDocumentTreeService.QueryPath(roots, "logging.level");
        Assert.Equal("debug", logLevel);

        var missing = StructuredDocumentTreeService.QueryPath(roots, "server.database.user");
        Assert.Null(missing);
    }
}
