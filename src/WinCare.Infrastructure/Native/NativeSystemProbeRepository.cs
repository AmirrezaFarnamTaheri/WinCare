using System;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Telemetry;

namespace WinCare.Infrastructure.Native;

/// <summary>
/// Zero-allocation managed repository wrapping native C-ABI kernel probes and cleaners.
/// </summary>
public sealed class NativeSystemProbeRepository : INativeSystemProbeRepository
{
    private readonly CleanTempFilesNativeCall _cleanTempFiles;

    /// <summary>Initializes the repository with the production native cleaner.</summary>
    public unsafe NativeSystemProbeRepository()
        : this(WinCareCoreNative.WinCareCleanTempFiles)
    {
    }

    /// <summary>Initializes the repository with a native-call seam for adapter tests.</summary>
    internal NativeSystemProbeRepository(CleanTempFilesNativeCall cleanTempFiles)
    {
        ArgumentNullException.ThrowIfNull(cleanTempFiles);
        _cleanTempFiles = cleanTempFiles;
    }

    /// <inheritdoc/>
    public ValueTask<SystemSnapshot> GetSystemSnapshotAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        unsafe
        {
            NativeSysSnapshot raw = default;
            int status = WinCareCoreNative.WinCareSysSnapshotAll(&raw);
            if (status != 0)
            {
                throw new InvalidOperationException($"wincare_sys_snapshot_all failed with status code {status}.");
            }

            // F-018: per-metric validity is carried through instead of collapsing
            // unknown into zero-valued metrics.
            var snapshot = new SystemSnapshot(
                raw.CpuUsagePct,
                raw.RamUsedBytes,
                raw.RamTotalBytes,
                raw.DiskFreeBytes,
                raw.DiskTotalBytes,
                raw.NetActive != 0,
                CpuMetricValid: (raw.ValidMask & 0x1) != 0,
                RamMetricValid: (raw.ValidMask & 0x2) != 0,
                DiskMetricValid: (raw.ValidMask & 0x4) != 0,
                NetMetricValid: (raw.ValidMask & 0x8) != 0,
                DiskVolume: raw.DiskVolume == 0 ? '\0' : (char)raw.DiskVolume);

            return ValueTask.FromResult(snapshot);
        }
    }

    /// <inheritdoc/>
    public ValueTask<CleanExecutionResult> CleanTempFilesAsync(bool dryRun, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        unsafe
        {
            NativeCleanResult raw = default;
            int status = _cleanTempFiles(dryRun ? (byte)1 : (byte)0, &raw);
            if (status != 0)
            {
                throw new InvalidOperationException($"wincare_clean_temp_files failed with status code {status}.");
            }

            var result = new CleanExecutionResult(
                raw.BytesReclaimed,
                raw.FilesRemoved,
                raw.ErrorCode);

            return ValueTask.FromResult(result);
        }
    }
}
