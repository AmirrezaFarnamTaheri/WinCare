using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinCare.App.Services;
using WinCare.App.ViewModels.Pages;
using WinCare.App.Views;

namespace WinCare.App.Views.Pages;

public sealed partial class HomePage : Page
{
    private const double HomeCompactBreakpointDip = 820;
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
        AppRuntime.Current.Journal.Changed += JournalChanged;
        AppRuntime.Current.PluginRegistry.RegistryChanged += RegistryChanged;
        _ = RefreshWidgetsAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _isVisible = false;
        _widgetRefreshVersion++;
        AppRuntime.Current.Journal.Changed -= JournalChanged;
        AppRuntime.Current.PluginRegistry.RegistryChanged -= RegistryChanged;
        base.OnNavigatedFrom(e);
    }

    private void JournalChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() => { if (_isVisible) ViewModel.RefreshActivity(AppRuntime.Current.Journal.GetAll()); });
    private void RegistryChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() => { if (_isVisible) _ = RefreshWidgetsAsync(); });

    private async Task RefreshWidgetsAsync()
    {
        int version = ++_widgetRefreshVersion;
        try
        {
            var widgets = await Task.Run(() => AppRuntime.Current.PluginRegistry.GetActivePluginWidgets());
            if (!_isVisible || version != _widgetRefreshVersion) return;
            PluginWidgets.PopulateWidgets(widgets);
            PluginWidgets.Visibility = widgets.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            PluginWidgetError.IsOpen = false;
        }
        catch (Exception ex)
        {
            if (!_isVisible || version != _widgetRefreshVersion) return;
            PluginWidgets.Visibility = Visibility.Collapsed;
            PluginWidgetError.Message = "One or more extension widgets could not be loaded. Open Extensions to review their current state.";
            PluginWidgetError.IsOpen = true;
            System.Diagnostics.Debug.WriteLine($"[HomePage] Widget refresh failed: {ex}");
        }
    }

    private void RunCheckupButton_Click(object sender, RoutedEventArgs e) => NavigateTo("checkup");
    private void ViewActivityButton_Click(object sender, RoutedEventArgs e) => NavigateTo("activity");
    private void BrowseToolsButton_Click(object sender, RoutedEventArgs e) => NavigateTo("all-tools");
    private void OpenExtensions_Click(object sender, RoutedEventArgs e) => NavigateTo("plugin-store");
    private void OpenTroubleshoot_Click(object sender, RoutedEventArgs e) => NavigateTo("ai-doctor");
    private void OpenCleanup_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateToSection(this, "system-care", "Clean up");
    private void OpenStartup_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateToSection(this, "system-care", "Apps & startup");
    private void OpenNetwork_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateToSection(this, "system-care", "Network & updates");
    private void NavCategory_System_Click(object sender, RoutedEventArgs e) => NavigateTo("checkup");
    private void NavCategory_Security_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateToSection(this, "security", "Status");
    private void NavCategory_Performance_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateToSection(this, "system-care", "Performance");
    private void NavCategory_Storage_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateToSection(this, "system-care", "Clean up");
    private void NavCategory_Updates_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateToSection(this, "system-care", "Network & updates");
    private void NavigateTo(string key) => PageNavigation.NavigateTo(this, key);

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < HomeCompactBreakpointDip;
        ViewModel.SetCompactLayout(compact);
        PageLayout.Padding = compact ? new Thickness(20, 20, 20, 28) : new Thickness(32, 28, 32, 36);
        HeroLayout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        HeroLayout.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(EvidenceSummaryCard, compact ? 0 : 1);
        Grid.SetRow(EvidenceSummaryCard, compact ? 1 : 0);
        RecommendationsGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        RecommendationsGrid.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        RecommendationsGrid.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(CleanupRecommendation, 0); Grid.SetRow(CleanupRecommendation, 0);
        Grid.SetColumn(StartupRecommendation, compact ? 0 : 1); Grid.SetRow(StartupRecommendation, compact ? 1 : 0);
        Grid.SetColumn(NetworkRecommendation, compact ? 0 : 2); Grid.SetRow(NetworkRecommendation, compact ? 2 : 0);
        OverviewGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        OverviewGrid.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(RecentActivityCard, compact ? 0 : 1); Grid.SetRow(RecentActivityCard, compact ? 1 : 0);
        ExploreActions.Orientation = compact ? Orientation.Vertical : Orientation.Horizontal;
    }
}
