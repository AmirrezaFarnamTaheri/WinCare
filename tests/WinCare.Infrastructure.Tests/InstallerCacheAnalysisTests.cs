using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Commands;
using Catalog = WinCare.CommandCatalog.CommandCatalog;

namespace WinCare.Infrastructure.Tests;

public sealed class InstallerCacheAnalysisTests
{
    [Fact]
    public async Task Analysis_retains_unmatched_files_and_reports_registration_evidence()
    {
        using var executor = new WindowsCommandExecutor();
        executor.InstallerCacheEnumerationSeam = (_, _, _) => new WindowsCommandExecutor.InstallerCacheEnumeration(
            [new WindowsCommandExecutor.InstallerCacheFile(@"C:\\Windows\\Installer\\registered.msi", 42, DateTimeOffset.UnixEpoch),
             new WindowsCommandExecutor.InstallerCacheFile(@"C:\\Windows\\Installer\\unmatched.msp", 7, DateTimeOffset.UnixEpoch)],
            Truncated: false, Errors: []);
        executor.InstallerRegistrationEvidenceSeam = _ => new WindowsCommandExecutor.InstallerRegistrationEvidence(
            [new WindowsCommandExecutor.InstallerRegistration(@"C:\\Windows\\Installer\\registered.msi", "product", "HKLM[Registry64]\\...\\InstallProperties")],
            Truncated: false, Errors: []);

        CommandHandlerOutcome outcome = await executor.ExecuteAsync(
            Catalog.Find("installer-cache-analysis")!,
            CommandRequest.Preview("installer-cache-analysis", JsonSerializer.SerializeToElement(new { MaxFiles = 2 })),
            CancellationToken.None);

        Assert.Equal("installer-cache-analysis.ok", outcome.Code);
        JsonElement files = outcome.Data!.Value.GetProperty("files");
        Assert.Equal("registered", files[0].GetProperty("registrationState").GetString());
        Assert.Equal("retained", files[0].GetProperty("disposition").GetString());
        Assert.Equal("unmatched", files[1].GetProperty("registrationState").GetString());
        Assert.Equal("retained", files[1].GetProperty("disposition").GetString());
        Assert.Contains("never declared safe", outcome.Data!.Value.GetProperty("safety").GetString());
    }

    [Fact]
    public async Task Analysis_marks_truncated_or_access_error_evidence_incomplete()
    {
        using var executor = new WindowsCommandExecutor();
        executor.InstallerCacheEnumerationSeam = (_, _, _) => new WindowsCommandExecutor.InstallerCacheEnumeration([], Truncated: true, ["Access denied"]);
        executor.InstallerRegistrationEvidenceSeam = _ => new WindowsCommandExecutor.InstallerRegistrationEvidence([], Truncated: false, []);

        CommandHandlerOutcome outcome = await executor.ExecuteAsync(
            Catalog.Find("installer-cache-analysis")!, CommandRequest.Preview("installer-cache-analysis"), CancellationToken.None);

        Assert.False(outcome.Data!.Value.GetProperty("scanComplete").GetBoolean());
        Assert.True(outcome.Data!.Value.GetProperty("cacheEnumerationTruncated").GetBoolean());
        Assert.Equal("Access denied", outcome.Data!.Value.GetProperty("cacheEnumerationErrors")[0].GetString());
    }
}
