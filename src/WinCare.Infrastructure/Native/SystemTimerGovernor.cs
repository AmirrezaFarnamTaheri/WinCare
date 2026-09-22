using System.Runtime.InteropServices;

namespace WinCare.Infrastructure.Native;

public static class SystemTimerGovernor
{
    [DllImport("ntdll.dll")]
    private static extern int NtSetTimerResolution(uint desiredResolution, [MarshalAs(UnmanagedType.Bool)] bool setResolution, out uint currentResolution);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryTimerResolution(out uint minimumResolution, out uint maximumResolution, out uint actualResolution);

    public static (bool Success, uint CurrentResolution100Ns) EnableHighPrecisionTimer()
    {
        int status = NtSetTimerResolution(5000, true, out uint currentRes);
        return (status == 0, currentRes);
    }

    public static void RestoreDefaultTimer()
    {
        NtSetTimerResolution(0, false, out _);
    }
}
