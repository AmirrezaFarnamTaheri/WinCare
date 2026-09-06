using System.Threading.Tasks;
using WinCare.Domain.Telemetry;
using WinCare.Infrastructure.Native;
using Xunit;

namespace WinCare.Infrastructure.Tests;

public sealed class NativeSystemProbeRepositoryTests
{
    [Fact]
    public async Task GetSystemSnapshotAsync_ReturnsValidSystemMetrics()
    {
        INativeSystemProbeRepository repository = new NativeSystemProbeRepository();
        SystemSnapshot snapshot = await repository.GetSystemSnapshotAsync();

        Assert.NotNull(snapshot);
        Assert.True(snapshot.RamTotalBytes > 0, "RamTotalBytes should be greater than zero");
        Assert.True(snapshot.DiskTotalBytes > 0, "DiskTotalBytes should be greater than zero");
        Assert.InRange(snapshot.CpuUsagePct, 0.0f, 100.0f);
    }

    [Fact]
    public async Task CleanTempFilesAsync_DryRun_ReturnsValidCleanResultWithoutDeleting()
    {
        INativeSystemProbeRepository repository = new NativeSystemProbeRepository();
        CleanExecutionResult result = await repository.CleanTempFilesAsync(dryRun: true);

        Assert.NotNull(result);
        Assert.Equal(0, result.ErrorCode);
    }
}
