using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;

namespace WinCare.Infrastructure.Commands;

public sealed partial class WindowsCommandExecutor
{
    private CommandHandlerOutcome WindowInventory(CommandParameters p)
    {
        int limit = p.Int32("Limit", 500, 1, 5000);
        List<WindowRecord> windows = EnumerateWindows().Take(limit).ToList();
        return Success("windows", $"Enumerated {windows.Count} top-level windows.", windows);
    }

    private CommandHandlerOutcome WindowSearch(CommandParameters p)
    {
        string query = p.String("Query").Trim();
        int limit = p.Int32("Limit", 100, 1, 1000);
        IEnumerable<WindowRecord> windows = EnumerateWindows();
        if (query.Length > 0)
        {
            windows = windows.Where(w => w.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || w.ClassName.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        WindowRecord[] result = windows.Take(limit).ToArray();
        return Success("window-search", $"Found {result.Length} matching windows.", result);
    }

    private CommandHandlerOutcome MonitorInventory()
    {
        var rows = new List<object>();
        for (uint index = 0; index < 64; index++)
        {
            var device = new WindowsInterop.DisplayDevice { Cb = Marshal.SizeOf<WindowsInterop.DisplayDevice>() };
            if (!WindowsInterop.EnumDisplayDevices(null, index, ref device, 0)) break;
            rows.Add(new
            {
                index,
                device.DeviceName,
                device.DeviceString,
                device.DeviceId,
                device.DeviceKey,
                stateFlags = device.StateFlags,
                attachedToDesktop = (device.StateFlags & 0x1) != 0,
                primary = (device.StateFlags & 0x4) != 0,
            });
        }
        return Success("monitors", $"Enumerated {rows.Count} display adapters/monitors.", rows);
    }

    private CommandHandlerOutcome MonitorControls()
    {
        List<PhysicalMonitorRecord> monitors = EnumeratePhysicalMonitors();
        return Success("monitor-controls", $"Enumerated {monitors.Count} physical monitor control surfaces.", monitors);
    }

    private CommandHandlerOutcome DisplayPipeline()
    {
        return Success("display-pipeline", "Display pipeline inspected through user32 and DDC/CI.", new
        {
            displays = MonitorInventory().Data,
            physicalControls = MonitorControls().Data,
            capturedAt = DateTimeOffset.UtcNow,
        });
    }

    private CommandHandlerOutcome VirtualDisplayCapability()
    {
        var devices = new List<object>();
        for (uint index = 0; index < 128; index++)
        {
            var device = new WindowsInterop.DisplayDevice { Cb = Marshal.SizeOf<WindowsInterop.DisplayDevice>() };
            if (!WindowsInterop.EnumDisplayDevices(null, index, ref device, 0)) break;
            bool virtualLike = device.DeviceString.Contains("virtual", StringComparison.OrdinalIgnoreCase) || device.DeviceId.Contains("IDD", StringComparison.OrdinalIgnoreCase) || device.DeviceId.Contains("RDP", StringComparison.OrdinalIgnoreCase);
            if (virtualLike) devices.Add(new { index, device.DeviceName, device.DeviceString, device.DeviceId });
        }
        return Success("virtual-display-capability", "Virtual display capability inspected.", new { supported = devices.Count > 0, devices });
    }

    private CommandHandlerOutcome DisplayCalibrate(CommandParameters p)
    {
        int index = p.Int32("PhysicalIndex", 0, 0, 64);
        int? brightness = p.Contains("Brightness") ? p.Int32("Brightness", 0, 0, 100) : null;
        int? contrast = p.Contains("Contrast") ? p.Int32("Contrast", 0, 0, 100) : null;
        if (brightness is null && contrast is null) throw new CommandParameterException("Brightness", "Provide Brightness and/or Contrast in the 0-100 range.");
        List<(nint Handle, string Description)> handles = EnumeratePhysicalMonitorHandles();
        if (index >= handles.Count) throw new CommandParameterException("PhysicalIndex", $"PhysicalIndex {index} is out of range; {handles.Count} monitors are available.");
        (nint handle, string description) = handles[index];
        try
        {
            if (brightness.HasValue && !WindowsInterop.SetMonitorBrightness(handle, (uint)brightness.Value)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SetMonitorBrightness failed.");
            if (contrast.HasValue && !WindowsInterop.SetMonitorContrast(handle, (uint)contrast.Value)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SetMonitorContrast failed.");
        }
        finally
        {
            WindowsInterop.DestroyPhysicalMonitors(1, [new WindowsInterop.PhysicalMonitor { Handle = handle, Description = description }]);
        }
        return Success("display-calibrate", "Monitor controls updated through DDC/CI.", new { physicalIndex = index, description, brightness, contrast }, undo: false);
    }

    private CommandHandlerOutcome WindowTopmost(CommandParameters p)
    {
        nint hwnd = ParseWindowHandle(p);
        bool enabled = p.Boolean("Enabled", true);
        if (!WindowsInterop.SetWindowPos(hwnd, enabled ? WindowsInterop.HwndTopmost : WindowsInterop.HwndNotTopmost, 0, 0, 0, 0, WindowsInterop.SwpNoMove | WindowsInterop.SwpNoSize | WindowsInterop.SwpShowWindow))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SetWindowPos failed.");
        return Success("window-topmost", enabled ? "Window set always-on-top." : "Window removed from always-on-top.", new { handle = hwnd.ToInt64(), enabled }, undo: false);
    }

    private CommandHandlerOutcome WindowActivate(CommandParameters p)
    {
        nint hwnd = ParseWindowHandle(p);
        WindowsInterop.ShowWindow(hwnd, WindowsInterop.SwRestore);
        if (!WindowsInterop.SetForegroundWindow(hwnd)) return Block("window-activate", "Windows denied foreground activation for the selected window.");
        return Success("window-activate", "Window activated.", new { handle = hwnd.ToInt64() });
    }

    private CommandHandlerOutcome WindowZoneSet(CommandParameters p)
    {
        nint hwnd = ParseWindowHandle(p);
        int x = p.Int32("X", 0, -32768, 32768);
        int y = p.Int32("Y", 0, -32768, 32768);
        int width = p.Int32("Width", 800, 100, 16384);
        int height = p.Int32("Height", 600, 100, 16384);
        if (!WindowsInterop.SetWindowPos(hwnd, 0, x, y, width, height, WindowsInterop.SwpShowWindow)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SetWindowPos failed.");
        return Success("window-zone-set", "Window moved to requested bounds.", new { handle = hwnd.ToInt64(), x, y, width, height }, undo: false);
    }

    private CommandHandlerOutcome InputRelease()
    {
        ushort[] keys = [0x10, 0x11, 0x12, 0x5B, 0x5C]; // Shift/Ctrl/Alt/Win
        var inputs = new List<WindowsInterop.Input>();
        foreach (ushort key in keys)
        {
            inputs.Add(new WindowsInterop.Input { Type = 1, Data = new WindowsInterop.InputUnion { Keyboard = new WindowsInterop.KeyboardInput { VirtualKey = key, Flags = 0x0002 } } });
        }
        inputs.Add(new WindowsInterop.Input { Type = 0, Data = new WindowsInterop.InputUnion { Mouse = new WindowsInterop.MouseInput { Flags = 0x0004 } } });
        inputs.Add(new WindowsInterop.Input { Type = 0, Data = new WindowsInterop.InputUnion { Mouse = new WindowsInterop.MouseInput { Flags = 0x0010 } } });
        inputs.Add(new WindowsInterop.Input { Type = 0, Data = new WindowsInterop.InputUnion { Mouse = new WindowsInterop.MouseInput { Flags = 0x0040 } } });
        uint sent = WindowsInterop.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<WindowsInterop.Input>());
        if (sent != (uint)inputs.Count) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SendInput did not release every input state.");
        return Success("input-release", "Modifier keys and mouse buttons were released.", new { eventsSent = sent });
    }

    private CommandHandlerOutcome ColorCapture(CommandParameters p)
    {
        int x; int y;
        if (p.Contains("X") && p.Contains("Y")) { x = p.Int32("X"); y = p.Int32("Y"); }
        else
        {
            if (!WindowsInterop.GetCursorPos(out WindowsInterop.Point point)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "GetCursorPos failed.");
            x = point.X; y = point.Y;
        }
        nint hdc = WindowsInterop.GetDC(0);
        if (hdc == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "GetDC failed.");
        try
        {
            uint pixel = WindowsInterop.GetPixel(hdc, x, y);
            if (pixel == uint.MaxValue) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "GetPixel failed.");
            byte r = (byte)(pixel & 0xFF); byte g = (byte)((pixel >> 8) & 0xFF); byte b = (byte)((pixel >> 16) & 0xFF);
            return Success("color-capture", "Screen pixel color captured.", new { x, y, r, g, b, hex = $"#{r:X2}{g:X2}{b:X2}" });
        }
        finally { WindowsInterop.ReleaseDC(0, hdc); }
    }

    private async Task<CommandHandlerOutcome> ColorAddAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string value = p.RequiredString("Color").Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) throw new CommandParameterException("Color", "Color must be #RRGGBB or #RRGGBBAA.");
        string id = p.String("Id").Trim(); if (id.Length == 0) id = Guid.NewGuid().ToString("N");
        JsonElement item = Data(new { id, name = p.String("Name", value), color = value.ToUpperInvariant(), createdAt = DateTimeOffset.UtcNow });
        await AppendStateItemAsync("color-palette", item, cancellationToken).ConfigureAwait(false);
        return Success("color-add", "Color added to WinCare palette.", item, undo: false);
    }

    private CommandHandlerOutcome ExplorerSession()
    {
        WindowRecord[] explorer = EnumerateWindows().Where(w => w.ClassName.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase) || w.ClassName.Equals("ExploreWClass", StringComparison.OrdinalIgnoreCase)).ToArray();
        return Success("explorer-session", $"Found {explorer.Length} Explorer windows.", explorer);
    }

    private async Task<CommandHandlerOutcome> ExplorerSessionSaveAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string id = p.String("Id").Trim(); if (id.Length == 0) id = Guid.NewGuid().ToString("N");
        WindowRecord[] explorer = EnumerateWindows().Where(w => w.ClassName.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase) || w.ClassName.Equals("ExploreWClass", StringComparison.OrdinalIgnoreCase)).ToArray();
        JsonElement record = Data(new { id, name = p.String("Name", $"Explorer {DateTime.Now:g}"), savedAt = DateTimeOffset.UtcNow, windows = explorer });
        await AppendStateItemAsync("explorer-sessions", record, cancellationToken).ConfigureAwait(false);
        return Success("explorer-session-save", "Explorer window session recorded.", record, undo: false);
    }

    private async Task<CommandHandlerOutcome> ExplorerSessionRestoreAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string id = p.RequiredString("Id");
        JsonElement sessions = await _state.ReadArrayAsync("explorer-sessions", cancellationToken).ConfigureAwait(false);
        JsonElement? found = sessions.ValueKind == JsonValueKind.Array ? sessions.EnumerateArray().FirstOrDefault(x => x.TryGetProperty("id", out JsonElement e) && string.Equals(e.GetString(), id, StringComparison.Ordinal)).Clone() : null;
        if (found is null || found.Value.ValueKind == JsonValueKind.Undefined) return Block("explorer-session-restore", $"Explorer session '{id}' was not found.");
        int restored = 0;
        if (found.Value.TryGetProperty("windows", out JsonElement windows) && windows.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement window in windows.EnumerateArray())
            {
                string title = window.TryGetProperty("title", out JsonElement titleElement) ? titleElement.GetString() ?? string.Empty : string.Empty;
                string path = ExtractPathLikeText(title);
                if (path.Length == 0 || !Directory.Exists(path)) continue;
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = false, ArgumentList = { path } });
                restored++;
            }
        }
        return Success("explorer-session-restore", $"Restored {restored} Explorer locations from saved session.", new { id, restored });
    }

    private CommandHandlerOutcome UiAutomationSnapshot(CommandParameters p)
    {
        // Raw top-level HWND snapshot: no WPF UI Automation dependency.
        string query = p.String("Query");
        WindowRecord[] windows = EnumerateWindows().Where(w => query.Length == 0 || w.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(p.Int32("Limit", 200, 1, 1000)).ToArray();
        return Success("ui-automation-snapshot", "Top-level native accessibility/window snapshot captured without WPF dependencies.", windows);
    }

    private CommandHandlerOutcome DisplayOverrides()
    {
        using RegistryKey? graphics = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        return Success("peer-display-overrides", "User display override state read from registry.", new
        {
            logPixels = Convert.ToString(graphics?.GetValue("LogPixels"), CultureInfo.InvariantCulture),
            win8DpiScaling = Convert.ToString(graphics?.GetValue("Win8DpiScaling"), CultureInfo.InvariantCulture),
        });
    }

    private CommandHandlerOutcome DisplayOverridesReset()
    {
        using RegistryKey desktop = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop", writable: true);
        desktop.DeleteValue("LogPixels", throwOnMissingValue: false);
        desktop.DeleteValue("Win8DpiScaling", throwOnMissingValue: false);
        return Success("peer-display-reset", "Per-user DPI override registry values were removed. Sign out may be required.", new { reset = true }, undo: false);
    }

    private static nint ParseWindowHandle(CommandParameters p)
    {
        string text = p.RequiredString("Handle").Trim();
        long value;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) throw new CommandParameterException("Handle", "Handle is not valid hexadecimal.");
        }
        else if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) throw new CommandParameterException("Handle", "Handle must be a decimal or 0x-prefixed hexadecimal HWND.");
        if (value == 0) throw new CommandParameterException("Handle", "Handle must not be zero.");
        return new nint(value);
    }

    private static IEnumerable<WindowRecord> EnumerateWindows()
    {
        var rows = new List<WindowRecord>();
        WindowsInterop.EnumWindows((hwnd, _) =>
        {
            if (!WindowsInterop.IsWindowVisible(hwnd)) return true;
            var title = new StringBuilder(1024); WindowsInterop.GetWindowText(hwnd, title, title.Capacity);
            if (title.Length == 0) return true;
            var className = new StringBuilder(256); WindowsInterop.GetClassName(hwnd, className, className.Capacity);
            WindowsInterop.GetWindowRect(hwnd, out WindowsInterop.Rect rect);
            rows.Add(new WindowRecord(hwnd.ToInt64(), title.ToString(), className.ToString(), rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top)));
            return true;
        }, 0);
        return rows;
    }

    private static List<PhysicalMonitorRecord> EnumeratePhysicalMonitors()
    {
        List<(nint Handle, string Description)> handles = EnumeratePhysicalMonitorHandles();
        var rows = new List<PhysicalMonitorRecord>();
        foreach ((nint handle, string description) in handles)
        {
            try
            {
                uint minB = 0, curB = 0, maxB = 0, minC = 0, curC = 0, maxC = 0;
                bool hasBrightness = WindowsInterop.GetMonitorBrightness(handle, out minB, out curB, out maxB);
                bool hasContrast = WindowsInterop.GetMonitorContrast(handle, out minC, out curC, out maxC);
                rows.Add(new PhysicalMonitorRecord(description, hasBrightness, minB, curB, maxB, hasContrast, minC, curC, maxC));
            }
            finally { WindowsInterop.DestroyPhysicalMonitors(1, [new WindowsInterop.PhysicalMonitor { Handle = handle, Description = description }]); }
        }
        return rows;
    }

    private static List<(nint Handle, string Description)> EnumeratePhysicalMonitorHandles()
    {
        var rows = new List<(nint, string)>();
        WindowsInterop.EnumDisplayMonitors(0, 0, (nint monitor, nint hdc, ref WindowsInterop.Rect monitorRect, nint data) =>
        {
            if (!WindowsInterop.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out uint count) || count == 0 || count > 32) return true;
            var physical = new WindowsInterop.PhysicalMonitor[count];
            if (!WindowsInterop.GetPhysicalMonitorsFromHMONITOR(monitor, count, physical)) return true;
            foreach (WindowsInterop.PhysicalMonitor item in physical) rows.Add((item.Handle, item.Description ?? string.Empty));
            return true;
        }, 0);
        return rows;
    }

    private static string ExtractPathLikeText(string title)
    {
        string candidate = title.Trim();
        if (Directory.Exists(candidate)) return candidate;
        int separator = candidate.LastIndexOf(" - File Explorer", StringComparison.OrdinalIgnoreCase);
        if (separator > 0)
        {
            candidate = candidate[..separator].Trim();
            if (Directory.Exists(candidate)) return candidate;
        }
        return string.Empty;
    }

    private sealed record WindowRecord(long Handle, string Title, string ClassName, int X, int Y, int Width, int Height);
    private sealed record PhysicalMonitorRecord(string Description, bool BrightnessSupported, uint MinBrightness, uint CurrentBrightness, uint MaxBrightness, bool ContrastSupported, uint MinContrast, uint CurrentContrast, uint MaxContrast);
}
