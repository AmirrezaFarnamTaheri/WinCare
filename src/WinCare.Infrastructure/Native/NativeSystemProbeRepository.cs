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

            var snapshot = new SystemSnapshot(
                raw.CpuUsagePct,
                raw.RamUsedBytes,
                raw.RamTotalBytes,
                raw.DiskFreeBytes,
                raw.DiskTotalBytes,
                raw.NetActive != 0);

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
            int status = WinCareCoreNative.WinCareCleanTempFiles(dryRun ? (byte)1 : (byte)0, &raw);
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
