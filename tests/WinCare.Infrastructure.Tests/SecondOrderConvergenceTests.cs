using WinCare.Infrastructure.Commands;
using Xunit;

namespace WinCare.Infrastructure.Tests;

public sealed class SecondOrderConvergenceTests
{
    [Fact]
    public void HardwareTuner_ReadPowerStatus_returns_consistent_structure()
    {
        var status = WindowsCommandExecutor.HardwareTunerHelper.ReadPowerStatus();

        Assert.NotNull(status);
        Assert.NotNull(status.Summary);
        Assert.NotNull(status.PowerSource);
        if (status.HasBattery)
        {
            Assert.True(status.BatteryLifePercent <= 100);
        }
    }

    [Fact]
    public void HardwareTuner_GetActivePowerScheme_resolves_valid_scheme()
    {
        var scheme = WindowsCommandExecutor.HardwareTunerHelper.GetActivePowerScheme();

        Assert.NotNull(scheme);
        Assert.False(string.IsNullOrWhiteSpace(scheme.ActiveSchemeGuid));
        Assert.False(string.IsNullOrWhiteSpace(scheme.FriendlyName));
        Assert.NotEmpty(scheme.AvailableSchemes);
    }

    [Fact]
    public void HardwareTuner_GetDisplayMetrics_returns_display_configuration()
    {
        var display = WindowsCommandExecutor.HardwareTunerHelper.GetDisplayMetrics();

        Assert.NotNull(display);
        Assert.False(string.IsNullOrWhiteSpace(display.DeviceName));
        Assert.False(string.IsNullOrWhiteSpace(display.Summary));
        if (display.Success)
        {
            Assert.True(display.Width > 0);
            Assert.True(display.Height > 0);
            Assert.True(display.RefreshRateHz > 0);
        }
    }

    [Fact]
    public void EnvironmentSanitizer_AuditPathEnvironment_identifies_entries_and_lengths()
    {
        var report = WindowsCommandExecutor.EnvironmentSanitizerHelper.AuditPathEnvironment();

        Assert.NotNull(report);
        Assert.True(report.TotalEntries >= 0);
        Assert.True(report.ValidEntries >= 0);
        Assert.False(string.IsNullOrWhiteSpace(report.Summary));
        Assert.NotNull(report.DeadPaths);
        Assert.NotNull(report.DuplicatePaths);
    }

    [Fact]
    public void EnvironmentSanitizer_ScanPackageManagerCaches_enumerates_standard_managers()
    {
        var caches = WindowsCommandExecutor.EnvironmentSanitizerHelper.ScanPackageManagerCaches();

        Assert.NotNull(caches);
        Assert.Equal(6, caches.Count);
        Assert.Contains(caches, c => c.ManagerName == "WinGet Packages");
        Assert.Contains(caches, c => c.ManagerName == "Electric Cache");
        Assert.Contains(caches, c => c.ManagerName == "Cargo Registry Cache");
    }

    [Fact]
    public void ShellExtensibility_AuditContextMenuHandlers_finds_registered_handlers()
    {
        var report = WindowsCommandExecutor.ShellExtensibilityHelper.AuditContextMenuHandlers();

        Assert.NotNull(report);
        Assert.True(report.TotalHandlers >= 0);
        Assert.NotNull(report.Handlers);
        Assert.False(string.IsNullOrWhiteSpace(report.Summary));
    }

    [Fact]
    public void ShellExtensibility_QueryWindows11ClassicContextMenuState_returns_valid_policy()
    {
        var policy = WindowsCommandExecutor.ShellExtensibilityHelper.QueryWindows11ClassicContextMenuState();

        Assert.NotNull(policy);
        Assert.False(string.IsNullOrWhiteSpace(policy.PolicyKeyPath));
        Assert.False(string.IsNullOrWhiteSpace(policy.Summary));
    }

    [Fact]
    public void StorageDeduplication_ScanDuplicateCandidates_detects_identical_test_files()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wincare_dedup_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            byte[] sampleContent = new byte[8192];
            new Random(42).NextBytes(sampleContent);

            File.WriteAllBytes(Path.Combine(tempDir, "fileA.bin"), sampleContent);
            File.WriteAllBytes(Path.Combine(tempDir, "fileB.bin"), sampleContent);
            File.WriteAllBytes(Path.Combine(tempDir, "fileC_different.bin"), new byte[8192]);

            var report = WindowsCommandExecutor.StorageDeduplicationHelper.ScanDuplicateCandidates(tempDir);

            Assert.NotNull(report);
            Assert.Equal(3, report.FilesAudited);
            Assert.Equal(1, report.DuplicateGroupsCount);
            Assert.Equal(2, report.TotalDuplicateFiles);
            Assert.Equal(8192UL, report.PotentialReclaimableBytes);
            Assert.Single(report.DuplicateSets);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void BinaryDiagnostics_InspectPeHeaders_parses_dotnet_executable_headers()
    {
        string currentExe = Environment.ProcessPath ?? typeof(SecondOrderConvergenceTests).Assembly.Location;
        var peReport = WindowsCommandExecutor.BinaryDiagnosticsHelper.InspectPeHeaders(currentExe);

        Assert.NotNull(peReport);
        Assert.True(peReport.IsValidPe);
        Assert.False(string.IsNullOrWhiteSpace(peReport.Architecture));
        Assert.True(peReport.HasAslr);
        Assert.True(peReport.HasDep);
        Assert.False(string.IsNullOrWhiteSpace(peReport.Summary));
    }

    [Fact]
    public void BinaryDiagnostics_ScanCrashDumps_returns_valid_inventory()
    {
        var report = WindowsCommandExecutor.BinaryDiagnosticsHelper.ScanCrashDumps();

        Assert.NotNull(report);
        Assert.True(report.TotalDumps >= 0);
        Assert.NotNull(report.Dumps);
        Assert.False(string.IsNullOrWhiteSpace(report.Summary));
    }
}
