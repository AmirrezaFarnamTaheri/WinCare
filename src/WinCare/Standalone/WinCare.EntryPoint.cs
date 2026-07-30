using System;
using System.Linq;
using System.Threading;

internal static class WinCareEntryPoint
{
#if WINCARE_GUI
    [STAThread]
#endif
    public static int Main(string[] args)
    {
        // Help must return before payload extraction so it remains fast, side-effect free, and available without PowerShell.
        if (args.Any(argument => string.Equals(argument, "-?", StringComparison.OrdinalIgnoreCase) || string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Out.WriteLine("Usage: WinCare[.exe] [-Command <name>] [-ArgumentsJson <json>] [-Theme <name>] [-Apply] [-Json] [-ReadOnly] [-Ascii] [-NoLogo]");
            return 0;
        }

        using var launchMutex = new Mutex(false, @"Local\WinCare.Standalone.Launch");
        var ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = launchMutex.WaitOne(TimeSpan.FromMinutes(2));
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }
            if (!ownsMutex)
                throw new TimeoutException("Timed out waiting for the WinCare standalone launch lock.");
            return WinCareHost.Main(args);
        }
        finally
        {
            if (ownsMutex)
                launchMutex.ReleaseMutex();
        }
    }
}
