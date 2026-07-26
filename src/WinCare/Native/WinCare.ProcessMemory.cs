using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinCare.Native;

public sealed class WorkingSetTrimResult
{
    public bool Success { get; init; }
    public bool IdentityMatched { get; init; }
    public int ProcessId { get; init; }
    public long ProcessStartTimeUtcTicks { get; init; }
    public long BeforeBytes { get; init; }
    public long AfterBytes { get; init; }
    public int Win32Error { get; init; }
    public string Message { get; init; } = string.Empty;
}

public static class ProcessMemory
{
    [Flags]
    private enum ProcessAccess : uint
    {
        SetQuota = 0x0100,
        QueryLimitedInformation = 0x1000,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;

        public long ToLong() => ((long)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        ProcessAccess desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        SafeProcessHandle process,
        out FileTime creation,
        out FileTime exit,
        out FileTime kernel,
        out FileTime user);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(
        SafeProcessHandle process,
        ref ProcessMemoryCounters counters,
        uint size);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(SafeProcessHandle process);

    public static WorkingSetTrimResult TrimWorkingSet(
        int processId,
        long expectedStartTimeUtcTicks,
        long minimumWorkingSetBytes)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        if (expectedStartTimeUtcTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedStartTimeUtcTicks));
        }

        if (minimumWorkingSetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumWorkingSetBytes));
        }

        using SafeProcessHandle process = OpenProcess(
            ProcessAccess.QueryLimitedInformation | ProcessAccess.SetQuota,
            inheritHandle: false,
            processId);

        if (process.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            return Failure(processId, expectedStartTimeUtcTicks, error, new Win32Exception(error).Message);
        }

        if (!GetProcessTimes(process, out FileTime creation, out _, out _, out _))
        {
            int error = Marshal.GetLastWin32Error();
            return Failure(processId, expectedStartTimeUtcTicks, error, new Win32Exception(error).Message);
        }

        long actualStartTicks = DateTime.FromFileTimeUtc(creation.ToLong()).Ticks;
        if (actualStartTicks != expectedStartTimeUtcTicks)
        {
            return new WorkingSetTrimResult
            {
                Success = false,
                IdentityMatched = false,
                ProcessId = processId,
                ProcessStartTimeUtcTicks = actualStartTicks,
                Message = "The process identity changed after preview.",
            };
        }

        if (!TryGetWorkingSet(process, out long beforeBytes, out int beforeError))
        {
            return Failure(processId, actualStartTicks, beforeError, new Win32Exception(beforeError).Message);
        }

        if (beforeBytes < minimumWorkingSetBytes)
        {
            return new WorkingSetTrimResult
            {
                Success = false,
                IdentityMatched = true,
                ProcessId = processId,
                ProcessStartTimeUtcTicks = actualStartTicks,
                BeforeBytes = beforeBytes,
                AfterBytes = beforeBytes,
                Message = "The process working set fell below the approved preview threshold.",
            };
        }

        if (!EmptyWorkingSet(process))
        {
            int error = Marshal.GetLastWin32Error();
            return new WorkingSetTrimResult
            {
                Success = false,
                IdentityMatched = true,
                ProcessId = processId,
                ProcessStartTimeUtcTicks = actualStartTicks,
                BeforeBytes = beforeBytes,
                AfterBytes = beforeBytes,
                Win32Error = error,
                Message = new Win32Exception(error).Message,
            };
        }

        if (!TryGetWorkingSet(process, out long afterBytes, out int afterError))
        {
            return new WorkingSetTrimResult
            {
                Success = false,
                IdentityMatched = true,
                ProcessId = processId,
                ProcessStartTimeUtcTicks = actualStartTicks,
                BeforeBytes = beforeBytes,
                Win32Error = afterError,
                Message = $"The trim succeeded, but the post-state could not be measured: {new Win32Exception(afterError).Message}",
            };
        }

        return new WorkingSetTrimResult
        {
            Success = true,
            IdentityMatched = true,
            ProcessId = processId,
            ProcessStartTimeUtcTicks = actualStartTicks,
            BeforeBytes = beforeBytes,
            AfterBytes = afterBytes,
            Message = "Windows accepted the bounded working-set trim and the process was re-measured.",
        };
    }

    private static bool TryGetWorkingSet(SafeProcessHandle process, out long bytes, out int error)
    {
        var counters = new ProcessMemoryCounters
        {
            Size = checked((uint)Marshal.SizeOf<ProcessMemoryCounters>()),
        };

        if (!GetProcessMemoryInfo(process, ref counters, counters.Size))
        {
            bytes = 0;
            error = Marshal.GetLastWin32Error();
            return false;
        }

        bytes = checked((long)counters.WorkingSetSize);
        error = 0;
        return true;
    }

    private static WorkingSetTrimResult Failure(int processId, long startTicks, int error, string message) =>
        new()
        {
            Success = false,
            IdentityMatched = false,
            ProcessId = processId,
            ProcessStartTimeUtcTicks = startTicks,
            Win32Error = error,
            Message = message,
        };
}
