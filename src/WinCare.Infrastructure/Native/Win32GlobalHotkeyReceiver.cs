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
    /// Registers a global hotkey with a callback action. The returned id is always allocated, but
    /// the overload with an <c>out bool registered</c> argument reports whether the operating system armed it: when
    /// registration fails the callback is dropped from the dispatch map so a caller can never
    /// mistake a dead id for a live hotkey.
    /// </summary>
    public static int Register(nint hWnd, uint modifiers, uint virtualKey, Action callback)
        => Register(hWnd, modifiers, virtualKey, callback, out _);

    public static int Register(nint hWnd, uint modifiers, uint virtualKey, Action callback, out bool registered)
    {
        ArgumentNullException.ThrowIfNull(callback);

        int id = Interlocked.Increment(ref _nextHotkeyId);
        _registeredCallbacks[id] = callback;

        registered = true;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            registered = TryRegisterHotKey(hWnd, id, modifiers | ModNoRepeat, virtualKey) ||
                         TryRegisterHotKey(hWnd, id, modifiers, virtualKey);
            if (!registered)
            {
                // Nothing will ever deliver a WM_HOTKEY for this id, so the callback would
                // otherwise linger in the dispatch map forever as an unusable hotkey.
                _registeredCallbacks.TryRemove(id, out _);
            }
        }

        return id;
    }

    /// <summary>
    /// Attempts a single registration, recording the Win32 reason when it does not succeed.
    /// </summary>
    private static bool TryRegisterHotKey(nint hWnd, int id, uint modifiers, uint virtualKey)
    {
        if (RegisterHotKey(hWnd, id, modifiers, virtualKey))
        {
            return true;
        }

        // SetLastError is declared on the import, so the failure reason is recoverable instead of
        // being silently swallowed into "hotkey did not register".
        int error = Marshal.GetLastWin32Error();
        System.Diagnostics.Debug.WriteLine(
            $"[Win32GlobalHotkeyReceiver] RegisterHotKey id {id} failed with Win32 error {error}: {new System.ComponentModel.Win32Exception(error).Message}");
        return false;
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
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or EntryPointNotFoundException or DllNotFoundException)
            {
                System.Diagnostics.Debug.WriteLine($"[Win32GlobalHotkeyReceiver] UnregisterHotKey id {hotkeyId} failed: {ex.Message}");
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
