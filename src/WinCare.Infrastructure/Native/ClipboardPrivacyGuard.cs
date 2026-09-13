using System.Runtime.InteropServices;
using System.Text;

namespace WinCare.Infrastructure.Native;

/// <summary>
/// Hardened Windows 10/11 Clipboard privacy guard.
/// Ensures sensitive tokens, master passphrases, and recovery keys are never
/// captured by Windows Clipboard History (Win+V) or uploaded to Microsoft Cloud Clipboard.
/// Incorporates multi-pass volatile memory wiping upon clearing.
/// </summary>
public static class ClipboardPrivacyGuard
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint GMEM_ZEROINIT = 0x0040;

    // Windows 10+ Privacy and History exclusion formats (PasswordSafe / Microsoft Specs)
    private static readonly uint CfExcludeClipboardContentFromMonitorProcessing =
        RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint CfCanIncludeInClipboardHistory =
        RegisterClipboardFormat("CanIncludeInClipboardHistory");
    private static readonly uint CfCanUploadToCloudClipboard =
        RegisterClipboardFormat("CanUploadToCloudClipboard");
    private static readonly uint CfClipboardViewerIgnore =
        RegisterClipboardFormat("Clipboard Viewer Ignore");

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    /// <summary>
    /// Sets sensitive text into the Windows clipboard while explicitly marking it
    /// to be ignored by clipboard history, monitor processing, and cloud sync.
    /// </summary>
    public static bool SetSensitiveText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return ClearClipboard();
        }

        const int maxAttempts = 10;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();

                    // Set Unicode text data
                    byte[] textBytes = Encoding.Unicode.GetBytes(text + "\0");
                    IntPtr hText = AllocAndCopy(textBytes);
                    if (hText != IntPtr.Zero)
                    {
                        SetClipboardData(CF_UNICODETEXT, hText);
                    }

                    // Set privacy flags (DWORD = 0)
                    byte[] zeroDword = [0, 0, 0, 0];
                    byte[] oneDword = [1, 0, 0, 0];

                    if (CfCanIncludeInClipboardHistory != 0)
                    {
                        IntPtr hHistory = AllocAndCopy(zeroDword);
                        if (hHistory != IntPtr.Zero) SetClipboardData(CfCanIncludeInClipboardHistory, hHistory);
                    }

                    if (CfCanUploadToCloudClipboard != 0)
                    {
                        IntPtr hCloud = AllocAndCopy(zeroDword);
                        if (hCloud != IntPtr.Zero) SetClipboardData(CfCanUploadToCloudClipboard, hCloud);
                    }

                    if (CfExcludeClipboardContentFromMonitorProcessing != 0)
                    {
                        IntPtr hMonitor = AllocAndCopy(oneDword);
                        if (hMonitor != IntPtr.Zero) SetClipboardData(CfExcludeClipboardContentFromMonitorProcessing, hMonitor);
                    }

                    if (CfClipboardViewerIgnore != 0)
                    {
                        IntPtr hViewer = AllocAndCopy(oneDword);
                        if (hViewer != IntPtr.Zero) SetClipboardData(CfClipboardViewerIgnore, hViewer);
                    }

                    return true;
                }
                finally
                {
                    CloseClipboard();
                }
            }

            Thread.Sleep(20);
        }

        return false;
    }

    /// <summary>
    /// Empties the system clipboard and burns any residual stack memory.
    /// </summary>
    public static bool ClearClipboard()
    {
        const int maxAttempts = 10;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    unsafe
                    {
                        WinCareCoreNative.WinCareCoreBurnStack(128);
                    }
                    return true;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            Thread.Sleep(20);
        }
        return false;
    }

    /// <summary>
    /// Performs three-pass memory wiping on a managed byte array in-place.
    /// </summary>
    public static void SecureTrashMemory(byte[] buffer)
    {
        if (buffer == null || buffer.Length == 0) return;

        unsafe
        {
            fixed (byte* ptr = buffer)
            {
                WinCareCoreNative.WinCareCoreSecureTrashMemory(ptr, (nuint)buffer.Length);
            }
        }
    }

    private static IntPtr AllocAndCopy(byte[] data)
    {
        IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (UIntPtr)data.Length);
        if (hMem == IntPtr.Zero) return IntPtr.Zero;

        IntPtr pMem = GlobalLock(hMem);
        if (pMem == IntPtr.Zero)
        {
            GlobalFree(hMem);
            return IntPtr.Zero;
        }

        Marshal.Copy(data, 0, pMem, data.Length);
        GlobalUnlock(hMem);
        return hMem;
    }
}
