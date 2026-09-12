using System.Threading;
using System.Threading.Tasks;

namespace WinCare.Domain.Telemetry;

/// <summary>
/// Immutable snapshot of low-level system metrics captured via native C-ABI kernel probes.
/// </summary>
public sealed record SystemSnapshot(
    float CpuUsagePct,
    ulong RamUsedBytes,
    ulong RamTotalBytes,
    ulong DiskFreeBytes,
    ulong DiskTotalBytes,
    bool NetActive,
    // Per-metric validity and probed volume letter carried through FFI.
    bool CpuMetricValid = false,
    bool RamMetricValid = false,
    bool DiskMetricValid = false,
    bool NetMetricValid = false,
    char DiskVolume = '\0');

/// <summary>
/// Outcome of native safe file cleaning or dry-run inspection.
/// </summary>
public sealed record CleanExecutionResult(
    ulong BytesReclaimed,
    uint FilesRemoved,
    int ErrorCode);

/// <summary>
/// Domain repository interface for native zero-allocation system telemetry and safe cleaner operations.
/// </summary>
public interface INativeSystemProbeRepository
{
    ValueTask<SystemSnapshot> GetSystemSnapshotAsync(CancellationToken ct = default);
    ValueTask<CleanExecutionResult> CleanTempFilesAsync(bool dryRun, CancellationToken ct = default);
}
