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
using WinCare.App.ViewModels;
using WinCare.Application.Navigation;

namespace WinCare.App;

public sealed partial class MainWindow : Window
{
    // Source-Driven Development Citation:
    // Pattern: Windows App SDK AppWindow screen-coordinate positioning and DisplayArea bounds validation
    // Source: https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindow
    // Source: https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.displayarea
    // Source: https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.overlappedpresenter
    // "AppWindow provides native screen coordinates and presenter state without requiring manual Win32 P/Invoke window placement."

    /// <summary>
    /// Shell access for testing navigation routes.
    /// </summary>
    public Views.ShellPage ShellPage => Shell;
    private readonly Windows.UI.ViewManagement.AccessibilitySettings _accessibilitySettings = new();
    private bool _highContrastEventRegistered;
    public bool IsClosed { get; private set; }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    public MainWindow()
    {
        InitializeComponent();
        WindowRoot.ActualThemeChanged += (_, _) => RefreshThemeResources();
        try
        {
            _accessibilitySettings.HighContrastChanged += OnHighContrastChanged;
            _highContrastEventRegistered = true;
        }
        catch (COMException)
        {
        }
        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated) RefreshThemeResources();
        };
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
        if (_highContrastEventRegistered) _accessibilitySettings.HighContrastChanged -= OnHighContrastChanged;
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
        try
        {
            Services.AppRuntime.Current.ShutdownAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
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
        sender.ItemsSource = BuildGlobalSuggestions(sender.Text);
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
        GlobalSearchSuggestion? best = BuildGlobalSuggestions(query)
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

    private static IReadOnlyList<GlobalSearchSuggestion> BuildGlobalSuggestions(string? text)
    {
        string query = text?.Trim() ?? string.Empty;
        if (query.Length == 0) return [];
        var candidates = new List<(GlobalSearchSuggestion Item, int Score)>();
        foreach (NavigationDefinition route in NavigationCatalog.Items)
        {
            int score = ScoreSearch(query, route.Label, route.Id, string.Join(' ', route.Tabs));
            if (score > 0)
            {
                candidates.Add((new GlobalSearchSuggestion(route.Label, route.IsHidden ? "WinCare information" : "Open this area", route.Id, null, GlobalSearchSuggestionKind.Page), score + 30));
            }
        }
        foreach (var tool in AppRuntime.Current.ToolCatalog.All)
        {
            int score = ScoreSearch(query, tool.Title, tool.Summary, tool.Area, tool.Section, tool.Id, string.Join(' ', tool.Keywords));
            if (score <= 0) continue;
            candidates.Add((new GlobalSearchSuggestion(tool.Title, $"{tool.Area} · {tool.Section}", "all-tools", tool.Id, GlobalSearchSuggestionKind.Tool), score));
        }
        try
        {
            foreach (var extension in AppRuntime.Current.PluginRegistry.GetAllPlugins())
            {
                int score = ScoreSearch(query, extension.Name, extension.Description, extension.Category, extension.Author, extension.Id);
                if (score <= 0) continue;
                candidates.Add((new GlobalSearchSuggestion(extension.Name, $"Extension · {extension.Category}", "plugin-store", extension.Name, GlobalSearchSuggestionKind.Extension), score + 10));
            }
        }
        catch { }
        (string Title, string Terms, string Route)[] helpTopics =
        [
            ("How reviews and approvals work", "review approval safety preview change risk", "help"),
            ("Keyboard shortcuts", "keyboard shortcut ctrl k ctrl f search", "help"),
            ("Find a tool", "find discover tool category power tools", "help"),
            ("Recent changes and results", "history evidence receipt report recent changes", "activity"),
            ("About WinCare", "about version license credits", "about"),
        ];
        foreach ((string title, string terms, string route) in helpTopics)
        {
            int score = ScoreSearch(query, title, terms);
            if (score <= 0) continue;
            candidates.Add((new GlobalSearchSuggestion(title, "Help topic", route, null, GlobalSearchSuggestionKind.Help), score + 5));
        }
        return candidates.OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Item.Title, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Item).DistinctBy(item => (item.Title, item.Route, item.Query)).Take(10).ToArray();
    }

    private static int ScoreSearch(string query, params string?[] fields)
    {
        string[] tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return 0;
        int score = 0;
        foreach (string token in tokens)
        {
            int tokenScore = 0;
            foreach (string? field in fields)
            {
                if (string.IsNullOrWhiteSpace(field)) continue;
                if (string.Equals(field, token, StringComparison.OrdinalIgnoreCase)) tokenScore = Math.Max(tokenScore, 100);
                else if (field.StartsWith(token, StringComparison.OrdinalIgnoreCase)) tokenScore = Math.Max(tokenScore, 70);
                else if (field.Contains(token, StringComparison.OrdinalIgnoreCase)) tokenScore = Math.Max(tokenScore, 35);
            }
            if (tokenScore == 0) return 0;
            score += tokenScore;
        }
        return score;
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
