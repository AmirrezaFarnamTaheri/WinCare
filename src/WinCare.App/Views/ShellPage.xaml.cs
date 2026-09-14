using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCare.App.Services;
using WinCare.App.ViewModels;
using WinCare.App.Views.Dialogs;

namespace WinCare.App.Views;

public sealed partial class ShellPage : Page
{
    private readonly PageService _pageService = new();
    private object? _pendingParameter;
    private bool _initialized;

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

    /// <summary>Opens global search.</summary>
    public void OpenGlobalSearch(string? query) => OpenNavigationItem("all-tools", query?.Trim() ?? string.Empty);

    /// <summary>Opens tool.</summary>
    public void OpenTool(ToolNavigationRequest request) => OpenNavigationItem("all-tools", request);

    /// <summary>Navigates to the requested product page.</summary>
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

    /// <summary>Shows tour.</summary>
    public async Task ShowTourAsync()
    {
        var dialog = new FirstRunTourDialog { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        AppPreferences.MarkFirstRunTourSeen();
    }

    /// <summary>Handles the page loaded event.</summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        NavigationViewItem home = PrimaryNavigation.MenuItems.OfType<NavigationViewItem>().First();
        PrimaryNavigation.SelectedItem = home;
        _pageService.Navigate(ContentFrame, "home");

        if (!AppPreferences.HasSeenFirstRunTour)
            await ShowTourAsync();
    }

    /// <summary>Handles the primary navigation selection changed event.</summary>
    private void PrimaryNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string key) return;

        object? parameter = _pendingParameter;
        _pendingParameter = null;
        _pageService.Navigate(ContentFrame, key, parameter);
    }

    /// <summary>Opens navigation item.</summary>
    private void OpenNavigationItem(string key, object? parameter)
    {
        NavigationViewItem target = FindNavigationItem(key)
            ?? throw new KeyNotFoundException($"Navigation item '{key}' is not visible in the shell.");
        OpenNavigationItem(target, key, parameter);
    }

    /// <summary>Opens navigation item.</summary>
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

    /// <summary>Finds navigation item.</summary>
    private NavigationViewItem? FindNavigationItem(string key) =>
        PrimaryNavigation.MenuItems
            .Concat(PrimaryNavigation.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .SingleOrDefault(item => string.Equals(item.Tag as string, key, StringComparison.Ordinal));
}
