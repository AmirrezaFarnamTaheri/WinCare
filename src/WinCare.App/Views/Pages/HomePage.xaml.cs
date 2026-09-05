using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCare.App.Views;
using WinCare.App.ViewModels.Pages;
using Microsoft.UI.Xaml.Navigation;
using WinCare.App.Services;

namespace WinCare.App.Views.Pages;

public sealed partial class HomePage : Page
{
    private const double CompactThreshold = 1100;
    private bool _isVisible;
    private int _widgetRefreshVersion;

    public HomePage()
    {
        ViewModel = new HomePageViewModel();
        InitializeComponent();
    }

    public HomePageViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _isVisible = true;
        ViewModel.RefreshActivity(AppRuntime.Current.Journal.GetAll());
        AppRuntime.Current.PluginRegistry.RegistryChanged += RegistryChanged;
        _ = RefreshWidgetsAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _isVisible = false;
        _widgetRefreshVersion++;
        AppRuntime.Current.PluginRegistry.RegistryChanged -= RegistryChanged;
        base.OnNavigatedFrom(e);
    }

    private void RegistryChanged(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(() => { if (_isVisible) _ = RefreshWidgetsAsync(); });

    private async Task RefreshWidgetsAsync()
    {
        int version = ++_widgetRefreshVersion;
        try
        {
            var widgets = await Task.Run(() => AppRuntime.Current.PluginRegistry.GetActivePluginWidgets());
            if (!_isVisible || version != _widgetRefreshVersion) return;
            PluginWidgets.PopulateWidgets(widgets);
            PluginWidgets.Visibility = widgets.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Widget refresh failed ({ex.GetType().Name}).");
        }
    }

    private void RunCheckupButton_Click(object sender, RoutedEventArgs e) => NavigateTo("checkup");

    private void ViewActivityButton_Click(object sender, RoutedEventArgs e) => NavigateTo("activity");

    private void BrowseToolsButton_Click(object sender, RoutedEventArgs e) => NavigateTo("all-tools");

    private void NavCategory_System_Click(object sender, RoutedEventArgs e) => NavigateTo("system-care");

    private void NavCategory_Security_Click(object sender, RoutedEventArgs e) => NavigateTo("security");

    private void NavCategory_Performance_Click(object sender, RoutedEventArgs e) => NavigateTo("checkup");

    private void NavCategory_Storage_Click(object sender, RoutedEventArgs e) => NavigateTo("system-care");

    private void NavCategory_Updates_Click(object sender, RoutedEventArgs e) => NavigateTo("repair-recovery");

    private void NavCategory_Activity_Click(object sender, RoutedEventArgs e) => NavigateTo("activity");

    private void NavigateTo(string key) => PageNavigation.NavigateTo(this, key);

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < CompactThreshold;
        ViewModel.SetCompactLayout(compact);
        PageLayout.Padding = compact ? new Thickness(20) : new Thickness(40, 30, 40, 40);
        HeroLayout.ColumnDefinitions[0].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(380);
        HeroLayout.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        SecondaryLayout.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(StatusCard, compact ? 0 : 1);
        Grid.SetRow(StatusCard, compact ? 1 : 0);
        Grid.SetColumn(SafetyCard, compact ? 0 : 1);
        Grid.SetRow(SafetyCard, compact ? 1 : 0);
        ActionLayout.Orientation = compact ? Orientation.Vertical : Orientation.Horizontal;
        int columns = compact ? 2 : 3;
        CategoryLayout.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        while (CategoryLayout.RowDefinitions.Count < 3) CategoryLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < CategoryLayout.Children.Count; i++)
        {
            Grid.SetColumn((FrameworkElement)CategoryLayout.Children[i], i % columns);
            Grid.SetRow((FrameworkElement)CategoryLayout.Children[i], i / columns);
        }
    }
}
