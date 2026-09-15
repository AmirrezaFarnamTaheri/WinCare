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

    public void OpenGlobalSearch(string? query) => OpenNavigationItem("all-tools", query?.Trim() ?? string.Empty);

    public void OpenTool(ToolNavigationRequest request) => OpenNavigationItem("all-tools", request);

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

        NavigationViewItem home = PrimaryNavigation.MenuItems.OfType<NavigationViewItem>().First();
        PrimaryNavigation.SelectedItem = home;
        _pageService.Navigate(ContentFrame, "home");

        if (!AppPreferences.HasSeenFirstRunTour)
            await ShowTourAsync();
    }

    private void PrimaryNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string key) return;

        object? parameter = _pendingParameter;
        _pendingParameter = null;
        _pageService.Navigate(ContentFrame, key, parameter);
    }

    private void OpenNavigationItem(string key, object? parameter)
    {
        NavigationViewItem target = FindNavigationItem(key)
            ?? throw new KeyNotFoundException($"Navigation item '{key}' is not visible in the shell.");
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

    private NavigationViewItem? FindNavigationItem(string key) =>
        PrimaryNavigation.MenuItems
            .Concat(PrimaryNavigation.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .SingleOrDefault(item => string.Equals(item.Tag as string, key, StringComparison.Ordinal));
}
