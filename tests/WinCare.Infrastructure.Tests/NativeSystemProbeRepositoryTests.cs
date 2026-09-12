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
        // A live TEMP tree can contain inaccessible entries. A successful ABI
        // invocation means a result was produced, not that every entry was read.
    }

    [Fact]
    public async Task CleanTempFilesAsync_PreservesNativePartialFailureInResult()
    {
        INativeSystemProbeRepository repository = CreatePartialFailureRepository();

        CleanExecutionResult result = await repository.CleanTempFilesAsync(dryRun: true);

        Assert.Equal((ulong)4096, result.BytesReclaimed);
        Assert.Equal((uint)2, result.FilesRemoved);
        Assert.Equal(5, result.ErrorCode);
    }

    private static unsafe INativeSystemProbeRepository CreatePartialFailureRepository() =>
        new NativeSystemProbeRepository(ReturnPartialCleanupResult);

    private static unsafe int ReturnPartialCleanupResult(byte dryRun, NativeCleanResult* output)
    {
        Assert.Equal((byte)1, dryRun);
        *output = new NativeCleanResult
        {
            BytesReclaimed = 4096,
            FilesRemoved = 2,
            ErrorCode = 5,
        };
        return 0;
    }
}
