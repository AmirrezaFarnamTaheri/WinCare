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
    bool NetActive);

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
