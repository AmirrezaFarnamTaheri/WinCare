using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WinCare.Infrastructure.Native;

public static class MemoryGovernor
{
    private const int MemoryPurgeStandbyList = 4;
    private const int MemoryEmptyWorkingSets = 2;

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int systemInformationClass, IntPtr systemInformation, int systemInformationLength);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public record MemoryMetrics(
        ulong TotalPhysicalBytes,
        ulong AvailablePhysicalBytes,
        uint MemoryLoadPercentage
    );

    public sealed record MemoryCompactionResult(
        bool StandbyRequestSucceeded,
        int WorkingSetTrimCount,
        int WorkingSetAttemptCount,
        long ObservedAvailableBytesChange)
    {
        public bool AnySucceeded => StandbyRequestSucceeded || WorkingSetTrimCount > 0;
    }

    public static MemoryMetrics GetSystemMemoryMetrics()
    {
        MEMORYSTATUSEX stat = new() { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref stat))
        {
            return new MemoryMetrics(stat.ullTotalPhys, stat.ullAvailPhys, stat.dwMemoryLoad);
        }
        return new MemoryMetrics(0, 0, 0);
    }

    public static Task<MemoryCompactionResult> CompactSystemMemoryAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            long before = (long)GetSystemMemoryMetrics().AvailablePhysicalBytes;
            bool standbySucceeded = false;

            GCHandle commandHandle = GCHandle.Alloc(MemoryPurgeStandbyList, GCHandleType.Pinned);
            try
            {
                standbySucceeded = NtSetSystemInformation(80, commandHandle.AddrOfPinnedObject(), sizeof(int)) == 0;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // Report unsupported operation; do not invent a successful purge.
            }
            finally
            {
                commandHandle.Free();
            }

            int attempted = 0;
            int trimmed = 0;
            try
            {
                foreach (Process process in Process.GetProcesses())
                {
                    using (process)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            if (!process.HasExited && process.Id > 4)
                            {
                                attempted++;
                                if (EmptyWorkingSet(process.Handle))
                                    trimmed++;
                            }
                        }
                        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or
                                                     InvalidOperationException or UnauthorizedAccessException)
                        {
                            // Each process may deny trimming; count only verified calls.
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or
                                         InvalidOperationException)
            {
                // Continue to report actual verified operations.
            }

            ct.ThrowIfCancellationRequested();
            long after = (long)GetSystemMemoryMetrics().AvailablePhysicalBytes;
            return new MemoryCompactionResult(standbySucceeded, trimmed, attempted, after - before);
        }, ct);
    }

}
