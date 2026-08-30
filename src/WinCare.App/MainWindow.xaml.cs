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
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    public MainWindow()
    {
        InitializeComponent();
        ApplyTheme(AppPreferences.Theme);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureBackdrop();
        ResizeWindow(1280, 800);
        WindowRoot.Loaded += OnWindowRootLoaded;
        Activated += OnWindowActivated;
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

    private void ConfigureBackdrop()
    {
        SystemBackdrop = MicaController.IsSupported()
            ? new MicaBackdrop { Kind = MicaKind.Base }
            : new DesktopAcrylicBackdrop();
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
        if (Uri.TryCreate(arguments, UriKind.Absolute, out var uri))
        {
            HandleProtocolActivation(uri);
        }
    }

    public void HandleProtocolActivation(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (
            !string.Equals(uri.Scheme, "wincare", StringComparison.OrdinalIgnoreCase) ||
            (uri.Host is not ("action" or "open")))
        {
            return;
        }

        string encodedSegment = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrEmpty(encodedSegment)) return;
        Shell.OpenGlobalSearch(Uri.UnescapeDataString(encodedSegment));
    }
}
