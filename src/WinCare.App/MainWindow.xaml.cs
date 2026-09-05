using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinCare.Infrastructure.Observability;
using WinCare.App.Services;

namespace WinCare.App;

public sealed partial class MainWindow : Window
{
    private const int SwShownormal = 1;
    private const int SwShowmaximized = 3;
    private const int WpfRestoreToMaximized = 0x0002;
    private const uint MonitorDefaultToNull = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public Point MinPosition;
        public Point MaxPosition;
        public Rect NormalPosition;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(nint windowHandle, ref WindowPlacement placement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(nint windowHandle, [In] ref WindowPlacement placement);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect([In] ref Rect rect, uint flags);

    public MainWindow()
    {
        InitializeComponent();
        ApplyTheme(AppPreferences.Theme);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureBackdrop();
        if (!AppPreferences.RememberWindowPlacement || !RestoreWindowPlacement())
        {
            ResizeWindow(1280, 800);
        }
        WindowRoot.Loaded += OnWindowRootLoaded;
        Activated += OnWindowActivated;
        Closed += OnWindowClosed;
    }

    public void ApplyTheme(string theme)
    {
        WindowRoot.RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    private void OnWindowRootLoaded(object sender, RoutedEventArgs e)
    {
        StartupTelemetry.Mark("FirstContentRendered");
        WindowRoot.Loaded -= OnWindowRootLoaded;
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        StartupTelemetry.Mark("ShellInteractive");
        Activated -= OnWindowActivated;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnWindowClosed;
        if (AppPreferences.RememberWindowPlacement)
        {
            PersistWindowPlacement();
        }
        try
        {
            AppPreferences.FlushAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Preference flush on close failed: {ex}");
        }
    }

    private void ConfigureBackdrop()
    {
        SystemBackdrop = MicaController.IsSupported()
            ? new MicaBackdrop { Kind = MicaKind.Base }
            : new DesktopAcrylicBackdrop();
    }

    private bool RestoreWindowPlacement()
    {
        WindowPlacementData? saved = AppPreferences.WindowPlacement;
        if (saved is null || !saved.IsUsable) return false;

        var rect = new Rect
        {
            Left = saved.Left,
            Top = saved.Top,
            Right = saved.Left + saved.Width,
            Bottom = saved.Top + saved.Height,
        };
        if (MonitorFromRect(ref rect, MonitorDefaultToNull) == nint.Zero) return false;

        nint handle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var placement = new WindowPlacement
        {
            Length = Marshal.SizeOf<WindowPlacement>(),
            Flags = 0,
            ShowCmd = saved.Maximized ? SwShowmaximized : SwShownormal,
            NormalPosition = rect,
        };
        return SetWindowPlacement(handle, ref placement);
    }

    private void PersistWindowPlacement()
    {
        try
        {
            nint handle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
            var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
            if (!GetWindowPlacement(handle, ref placement)) return;

            Rect bounds = placement.NormalPosition;
            var saved = new WindowPlacementData(
                bounds.Left,
                bounds.Top,
                bounds.Right - bounds.Left,
                bounds.Bottom - bounds.Top,
                placement.ShowCmd == SwShowmaximized || (placement.Flags & WpfRestoreToMaximized) != 0);
            if (saved.IsUsable) AppPreferences.SaveWindowPlacement(saved);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Window placement save failed: {ex}");
        }
    }

    private void ResizeWindow(int widthDips, int heightDips)
    {
        nint windowHandle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        double scale = GetDpiForWindow(windowHandle) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(widthDips * scale), (int)(heightDips * scale)));
    }

    private void SearchKeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        GlobalSearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void GlobalSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        Shell.OpenGlobalSearch(args.QueryText);
    }

    public void HandleProtocolActivation(string arguments)
    {
        if (Uri.TryCreate(arguments, UriKind.Absolute, out var uri)) HandleProtocolActivation(uri);
    }

    public void HandleProtocolActivation(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!string.Equals(uri.Scheme, "wincare", StringComparison.OrdinalIgnoreCase) ||
            (uri.Host is not ("action" or "open")))
        {
            return;
        }

        string encodedSegment = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrEmpty(encodedSegment)) return;
        Shell.OpenGlobalSearch(Uri.UnescapeDataString(encodedSegment));
    }
}
