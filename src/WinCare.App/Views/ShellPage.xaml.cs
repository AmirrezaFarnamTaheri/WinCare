using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCare.App.Services;
using WinCare.App.ViewModels;
using WinCare.App.Views.Dialogs;
using WinCare.Application.Navigation;

namespace WinCare.App.Views;

public sealed partial class ShellPage : Page
{
    private readonly PageService _pageService = new();
    private object? _pendingParameter;
    private bool _initialized;
    private bool _synchronizingNavigation;

    // Route keys live in NavigationCatalog; resolve the two the shell activates deep links
    // through once, so a catalog rename fails loudly here instead of silently breaking a route.
    private static readonly string HomeKey = NavigationCatalog.Items.Single(item => item.Id == "home").Id;
    private static readonly string AllToolsKey = NavigationCatalog.Items.Single(item => item.Id == "all-tools").Id;

    public ShellPage()
    {
        ViewModel = new ShellViewModel();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public ShellViewModel ViewModel { get; }

    private void Shell_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        PrimaryNavigation.PaneDisplayMode = e.NewSize.Width >= 920
            ? NavigationViewPaneDisplayMode.Left
            : e.NewSize.Width >= 680
                ? NavigationViewPaneDisplayMode.LeftCompact
                : NavigationViewPaneDisplayMode.LeftMinimal;
    }

    public void OpenGlobalSearch(string? query) => OpenNavigationItem(AllToolsKey, query?.Trim() ?? string.Empty);

    public void OpenTool(ToolNavigationRequest request) => OpenNavigationItem(AllToolsKey, request);

    public void NavigateTo(string key, object? parameter = null)
    {
        NavigationViewItem? target = FindNavigationItem(key);
        if (target is null)
        {
            _pendingParameter = null;
            PrimaryNavigation.SelectedItem = null;
            _pageService.Navigate(ContentFrame, key, parameter);
            return;
        }

        OpenNavigationItem(target, key, parameter);
    }

    public async Task ShowTourAsync()
    {
        var dialog = new FirstRunTourDialog { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        AppPreferences.MarkFirstRunTourSeen();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        // Look the home item up by route rather than trusting menu ordering.
        PrimaryNavigation.SelectedItem = FindNavigationItem(HomeKey);
        _pageService.Navigate(ContentFrame, HomeKey);

        if (!AppPreferences.HasSeenFirstRunTour && !App.IsCaptureSession)
        {
            // Capture sessions skip the tour so it never overlays a route being documented.
            // First run is the least error-tolerant moment of the session: a dialog fault must
            // not escape the loaded path and take the shell down with it.
            try
            {
                await ShowTourAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShellPage] First-run tour failed: {ex}");
            }
        }
    }

    private void PrimaryNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_synchronizingNavigation || args.SelectedItemContainer?.Tag is not string key) return;

        object? parameter = _pendingParameter;
        _pendingParameter = null;
        _pageService.Navigate(ContentFrame, key, parameter);
    }

    private void OpenNavigationItem(string key, object? parameter)
    {
        // A hidden route (about) or a renamed XAML Tag must not fault a deep link or a
        // wincare:// activation: fall back to direct frame navigation, which NavigateTo
        // already implements. Throwing is reserved for genuinely unknown routes at the
        // PageService boundary.
        NavigationViewItem? target = FindNavigationItem(key);
        if (target is null)
        {
            NavigateTo(key, parameter);
            return;
        }

        OpenNavigationItem(target, key, parameter);
    }

    private void OpenNavigationItem(NavigationViewItem target, string key, object? parameter)
    {
        if (ReferenceEquals(PrimaryNavigation.SelectedItem, target))
        {
            _pageService.Navigate(ContentFrame, key, parameter);
            return;
        }

        _pendingParameter = parameter;
        PrimaryNavigation.SelectedItem = target;
    }

    private void PrimaryNavigation_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (ContentFrame.CanGoBack) ContentFrame.GoBack();
    }

    private void ContentFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        PrimaryNavigation.IsBackEnabled = ContentFrame.CanGoBack;
        _synchronizingNavigation = true;
        try
        {
            string? key = _pageService.GetNavigationKey(e.SourcePageType);
            PrimaryNavigation.SelectedItem = key is null ? null : FindNavigationItem(key);
        }
        finally { _synchronizingNavigation = false; }
        if (PrimaryNavigation.PaneDisplayMode is NavigationViewPaneDisplayMode.LeftMinimal or NavigationViewPaneDisplayMode.LeftCompact)
            PrimaryNavigation.IsPaneOpen = false;
    }

    private NavigationViewItem? FindNavigationItem(string key) =>
        PrimaryNavigation.MenuItems
            .Concat(PrimaryNavigation.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, key, StringComparison.Ordinal));
}
