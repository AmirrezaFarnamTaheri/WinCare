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

    public static MemoryMetrics GetSystemMemoryMetrics()
    {
        MEMORYSTATUSEX stat = new() { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref stat))
        {
            return new MemoryMetrics(stat.ullTotalPhys, stat.ullAvailPhys, stat.dwMemoryLoad);
        }
        return new MemoryMetrics(0, 0, 0);
    }

    public static async Task<long> CompactSystemMemoryAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            long before = (long)GetSystemMemoryMetrics().AvailablePhysicalBytes;

            GCHandle commandHandle = GCHandle.Alloc(MemoryPurgeStandbyList, GCHandleType.Pinned);
            try
            {
                _ = NtSetSystemInformation(80, commandHandle.AddrOfPinnedObject(), sizeof(int));
            }
            catch
            {
            }
            finally
            {
                commandHandle.Free();
            }

            try
            {
                Process[] processes = Process.GetProcesses();
                foreach (var proc in processes)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        if (!proc.HasExited && proc.Id > 4)
                        {
                            EmptyWorkingSet(proc.Handle);
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }
            }
            catch
            {
            }

            long after = (long)GetSystemMemoryMetrics().AvailablePhysicalBytes;
            return Math.Max(0, after - before);
        }, ct);
    }
}
