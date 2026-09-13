using WinCare.Infrastructure.Commands;
using Xunit;

namespace WinCare.Infrastructure.Tests;

public sealed class PrivacyTelemetryHostsTests
{
    [Fact]
    public void WellKnownTelemetryDomains_ContainsExpectedDomains()
    {
        var domains = WindowsCommandExecutor.PrivacyHelper.WellKnownTelemetryDomains;

        Assert.NotEmpty(domains);
        Assert.True(domains.Count >= 30);
        Assert.Contains("vortex.data.microsoft.com", domains);
        Assert.Contains("telemetry.microsoft.com", domains);
        Assert.Contains("watson.telemetry.microsoft.com", domains);
    }

    [Fact]
    public void AuditHostsFile_NonExistentFile_ReturnsAllUnblocked()
    {
        string dummyPath = Path.Combine(Path.GetTempPath(), $"nonexistent_hosts_{Guid.NewGuid():N}.txt");

        WindowsCommandExecutor.TelemetryAuditResult result =
            WindowsCommandExecutor.PrivacyHelper.AuditHostsFile(dummyPath);

        Assert.False(result.FileExists);
        Assert.Equal(0, result.BlockedCount);
        Assert.Equal(WindowsCommandExecutor.PrivacyHelper.WellKnownTelemetryDomains.Count, result.UnblockedCount);
        Assert.Empty(result.BlockedDomains);
        Assert.Equal(WindowsCommandExecutor.PrivacyHelper.WellKnownTelemetryDomains.Count, result.UnblockedDomains.Count);
    }

    [Fact]
    public void AuditHostsFile_WithMockHostsFile_IdentifiesBlockedAndUnblocked()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"mock_hosts_{Guid.NewGuid():N}.txt");
        try
        {
            string[] lines =
            [
                "# Hosts file comment",
                "127.0.0.1 localhost",
                "0.0.0.0 vortex.data.microsoft.com",
                "127.0.0.1 telemetry.microsoft.com",
                "# Another comment",
                "   "
            ];
            File.WriteAllLines(tempFile, lines);

            WindowsCommandExecutor.TelemetryAuditResult result =
                WindowsCommandExecutor.PrivacyHelper.AuditHostsFile(tempFile);

            Assert.True(result.FileExists);
            Assert.Equal(2, result.BlockedCount);
            Assert.Equal(WindowsCommandExecutor.PrivacyHelper.WellKnownTelemetryDomains.Count - 2, result.UnblockedCount);
            Assert.Contains("vortex.data.microsoft.com", result.BlockedDomains);
            Assert.Contains("telemetry.microsoft.com", result.BlockedDomains);
            Assert.DoesNotContain("vortex.data.microsoft.com", result.UnblockedDomains);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
