using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCare.App.Services;
using WinCare.App.ViewModels;

namespace WinCare.App.Views;

public sealed partial class ShellPage : Page
{
    private readonly PageService _pageService = new();
    private string? _pendingSearch;

    public ShellPage()
    {
        ViewModel = new ShellViewModel();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public ShellViewModel ViewModel { get; }

    public void OpenGlobalSearch(string? query)
    {
        string normalized = query?.Trim() ?? string.Empty;
        NavigationViewItem target = PrimaryNavigation.MenuItems
            .OfType<NavigationViewItem>()
            .Single(item => string.Equals(item.Tag as string, "all-tools", StringComparison.Ordinal));

        _pendingSearch = normalized;
        if (ReferenceEquals(PrimaryNavigation.SelectedItem, target))
        {
            _pageService.Navigate(ContentFrame, "all-tools", normalized);
            _pendingSearch = null;
            return;
        }

        PrimaryNavigation.SelectedItem = target;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NavigationViewItem home = PrimaryNavigation.MenuItems.OfType<NavigationViewItem>().First();
        PrimaryNavigation.SelectedItem = home;
        _pageService.Navigate(ContentFrame, "home");
    }

    private void PrimaryNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string key)
        {
            return;
        }

        object? parameter = string.Equals(key, "all-tools", StringComparison.Ordinal) ? _pendingSearch : null;
        _pendingSearch = null;
        _pageService.Navigate(ContentFrame, key, parameter);
    }
}
