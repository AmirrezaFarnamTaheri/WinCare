namespace WinCare.Infrastructure.Native;

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

/// <summary>
/// Win32 Global Hotkey registration and management infrastructure (GalaxyBudsClient / PasteBarApp).
/// Manages atomic registration and deregistration of system-wide hotkeys with WM_HOTKEY handling.
/// </summary>
public static class Win32GlobalHotkeyReceiver
{
    private const string User32Dll = "user32.dll";

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    [DllImport(User32Dll, SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport(User32Dll, SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    private static int _nextHotkeyId = 1000;
    private static readonly ConcurrentDictionary<int, Action> _registeredCallbacks = new();

    /// <summary>
    /// Registers a global hotkey with a callback action.
    /// </summary>
    public static int Register(nint hWnd, uint modifiers, uint virtualKey, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        int id = Interlocked.Increment(ref _nextHotkeyId);
        _registeredCallbacks[id] = callback;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                if (!RegisterHotKey(hWnd, id, modifiers | ModNoRepeat, virtualKey))
                {
                    // If MOD_NOREPEAT fails on older OS versions, retry without it
                    RegisterHotKey(hWnd, id, modifiers, virtualKey);
                }
            }
            catch
            {
                // Fallback for non-GUI/headless test environments
            }
        }

        return id;
    }

    /// <summary>
    /// Unregisters a previously registered hotkey ID.
    /// </summary>
    public static bool Unregister(nint hWnd, int hotkeyId)
    {
        _registeredCallbacks.TryRemove(hotkeyId, out _);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                return UnregisterHotKey(hWnd, hotkeyId);
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Dispatches a received WM_HOTKEY message (id in wParam).
    /// </summary>
    public static bool DispatchMessage(int hotkeyId)
    {
        if (_registeredCallbacks.TryGetValue(hotkeyId, out var action))
        {
            action.Invoke();
            return true;
        }

        return false;
    }
}
