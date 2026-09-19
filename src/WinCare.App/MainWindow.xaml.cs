using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using WinCare.Infrastructure.Observability;
using WinCare.App.Services;
using WinCare.App.ViewModels;
using WinCare.Application.Navigation;

namespace WinCare.App;

public sealed partial class MainWindow : Window
{
    public Views.ShellPage ShellPage => Shell;
    private readonly Windows.UI.ViewManagement.AccessibilitySettings _accessibilitySettings = new();
    private bool _highContrastEventRegistered;
    public bool IsClosed { get; private set; }

    private readonly GlobalSearchService _globalSearch = new(AppRuntime.Current.ToolCatalog, AppRuntime.Current.PluginRegistry);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    public MainWindow()
    {
        InitializeComponent();
        WindowRoot.ActualThemeChanged += OnWindowRootThemeChanged;
        try
        {
            _accessibilitySettings.HighContrastChanged += OnHighContrastChanged;
            _highContrastEventRegistered = true;
        }
        catch (COMException)
        {
        }
        Activated += OnActivatedRefreshTheme;
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
        RefreshThemeResources();
        StartupTelemetry.Mark("FirstContentRendered");
        WindowRoot.Loaded -= OnWindowRootLoaded;
    }

    // Named so OnWindowClosed can unsubscribe: the discarded lambdas kept the closed window
    // alive on its own events and left RefreshThemeResources attached after close.
    private void OnWindowRootThemeChanged(FrameworkElement sender, object args) => RefreshThemeResources();

    private void OnActivatedRefreshTheme(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated) RefreshThemeResources();
    }

    private void RefreshThemeResources()
    {
        if (IsClosed) return;
        Converters.ThemeResourceBrushConverter.RefreshBrushes();
        bool dark = WindowRoot.ActualTheme == ElementTheme.Dark;
        AppWindow.TitleBar.ButtonForegroundColor = _accessibilitySettings.HighContrast
            ? new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground)
            : dark ? Colors.White : Colors.Black;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = _accessibilitySettings.HighContrast
            ? new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground)
            : dark ? Colors.LightGray : Colors.DimGray;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }

    private void OnHighContrastChanged(Windows.UI.ViewManagement.AccessibilitySettings sender, object args) =>
        DispatcherQueue.TryEnqueue(RefreshThemeResources);

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        StartupTelemetry.Mark("ShellInteractive");
        Activated -= OnWindowActivated;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        IsClosed = true;
        Closed -= OnWindowClosed;
        WindowRoot.ActualThemeChanged -= OnWindowRootThemeChanged;
        Activated -= OnActivatedRefreshTheme;
        if (_highContrastEventRegistered) _accessibilitySettings.HighContrastChanged -= OnHighContrastChanged;
        if (AppPreferences.RememberWindowPlacement) PersistWindowPlacement();
        // Window.Closed has no deferral API. Finish persistence before the final window
        // closes; neither helper requires the UI thread and runtime shutdown is bounded.
        FlushPreferencesAsync().GetAwaiter().GetResult();
        ShutdownRuntimeAsync().GetAwaiter().GetResult();
    }

    private static async Task FlushPreferencesAsync()
    {
        try
        {
            await AppPreferences.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Preference flush on close failed: {ex}");
        }
    }

    private static async Task ShutdownRuntimeAsync()
    {
        try
        {
            await Services.AppRuntime.Current.ShutdownAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Runtime shutdown on close failed: {ex}");
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
        var rect = new RectInt32(saved.Left, saved.Top, saved.Width, saved.Height);
        var displayArea = DisplayArea.GetFromRect(rect, DisplayAreaFallback.None);
        if (displayArea is null) return false;
        AppWindow.MoveAndResize(rect);
        if (saved.Maximized && AppWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
        return true;
    }

    private void PersistWindowPlacement()
    {
        try
        {
            PointInt32 pos = AppWindow.Position;
            SizeInt32 size = AppWindow.Size;
            bool isMaximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
            var saved = new WindowPlacementData(pos.X, pos.Y, size.Width, size.Height, isMaximized);
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
        GlobalSearchBox.IsSuggestionListOpen = !string.IsNullOrWhiteSpace(GlobalSearchBox.Text);
        args.Handled = true;
    }

    private void GlobalSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        sender.ItemsSource = _globalSearch.Search(sender.Text);
    }

    private void GlobalSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is GlobalSearchSuggestion chosen)
        {
            OpenSearchSuggestion(chosen);
            return;
        }

        string query = args.QueryText?.Trim() ?? string.Empty;
        if (query.Length == 0) return;
        GlobalSearchSuggestion? best = _globalSearch.Search(query)
            .FirstOrDefault(item => string.Equals(item.Title, query, StringComparison.OrdinalIgnoreCase));
        if (best is not null)
        {
            OpenSearchSuggestion(best);
            return;
        }

        Shell.OpenGlobalSearch(query);
    }

    private void OpenSearchSuggestion(GlobalSearchSuggestion suggestion)
    {
        GlobalSearchBox.Text = suggestion.Title;
        GlobalSearchBox.IsSuggestionListOpen = false;
        if (suggestion.Kind == GlobalSearchSuggestionKind.Tool)
        {
            Shell.OpenGlobalSearch(suggestion.Query ?? suggestion.Title);
            return;
        }

        Shell.NavigateTo(suggestion.Route, suggestion.Query);
    }

    public void HandleProtocolActivation(string arguments)
    {
        if (Uri.TryCreate(arguments, UriKind.Absolute, out var uri)) HandleProtocolActivation(uri);
    }

    public void HandleProtocolActivation(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!string.Equals(uri.Scheme, "wincare", StringComparison.OrdinalIgnoreCase) ||
            (uri.Host is not ("action" or "open"))) return;
        string encodedSegment = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrEmpty(encodedSegment)) return;
        Shell.OpenGlobalSearch(Uri.UnescapeDataString(encodedSegment));
    }
}
