using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinCare.App.Services;
using WinCare.App.ViewModels.Pages;
using WinCare.App.Views;
using WinCare.CommandCatalog.Models;

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
        WinCare.App.Animations.KineticSpringAnimation.AttachSpringHover(CleanupRecommendation, 1.015f);
        WinCare.App.Animations.KineticSpringAnimation.AttachSpringHover(StartupRecommendation, 1.015f);
        WinCare.App.Animations.KineticSpringAnimation.AttachSpringHover(NetworkRecommendation, 1.015f);
        WinCare.App.Animations.KineticSpringAnimation.AttachSpringHover(DownloadsRecommendation, 1.015f);
        WinCare.App.Animations.KineticSpringAnimation.AttachSpringHover(FilesRecommendation, 1.015f);
        WinCare.App.Animations.KineticSpringAnimation.AttachSpringHover(ProcessesRecommendation, 1.015f);
        WinCare.App.Animations.KineticSpringAnimation.AttachSpringHover(RunCheckupButton, 1.02f);
    }

    public HomePageViewModel ViewModel { get; }
    public ObservableCollection<HomeFavoriteTool> FavoriteTools { get; } = [];

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _isVisible = true;
        RefreshHomeCheckup();
        RefreshFavoriteTools();
        AppRuntime.Current.Journal.Changed += JournalChanged;
        AppRuntime.Current.Checkups.Changed += CheckupChanged;
        AppRuntime.Current.PluginRegistry.RegistryChanged += RegistryChanged;
        _ = RefreshWidgetsAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _isVisible = false;
        _widgetRefreshVersion++;
        AppRuntime.Current.Journal.Changed -= JournalChanged;
        AppRuntime.Current.Checkups.Changed -= CheckupChanged;
        AppRuntime.Current.PluginRegistry.RegistryChanged -= RegistryChanged;
        base.OnNavigatedFrom(e);
    }

    private void RefreshHomeCheckup() =>
        ViewModel.RefreshActivity(AppRuntime.Current.Journal.GetAll(), AppRuntime.Current.Checkups.ReadLatest());

    private void JournalChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() => { if (_isVisible) RefreshHomeCheckup(); });

    private void CheckupChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() => { if (_isVisible) RefreshHomeCheckup(); });
    private void RegistryChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_isVisible) return;
        RefreshFavoriteTools();
        _ = RefreshWidgetsAsync();
    });

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
            PluginWidgetError.Message = "Open Extensions to see which widget had a problem.";
            PluginWidgetError.IsOpen = true;
            System.Diagnostics.Debug.WriteLine($"[HomePage] Widget refresh failed: {ex}");
        }
    }

    private void RefreshFavoriteTools()
    {
        IReadOnlyDictionary<string, CommandDefinition> byId = AppRuntime.Current.ToolCatalog.All
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        HomeFavoriteTool[] desired = AppPreferences.FavoriteCommandIds
            .Where(byId.ContainsKey).Take(4)
            .Select(id =>
            {
                CommandDefinition command = byId[id];
                bool available = command.MigrationStatus is MigrationStatus.Implemented or MigrationStatus.BehaviorVerified;
                return new HomeFavoriteTool
                {
                    Id = command.Id,
                    Title = command.Title,
                    Subtitle = available ? command.Area + " · " + command.Section : "Unavailable in this build",
                    Available = available,
                };
            }).ToArray();

        FavoriteTools.Clear();
        foreach (HomeFavoriteTool tool in desired) FavoriteTools.Add(tool);
        FavoriteToolsPanel.Visibility = desired.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FavoriteTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HomeFavoriteTool selected })
            PageNavigation.OpenTool(this, selected.Id, JsonSerializer.SerializeToElement(new { }));
    }

    private void SeeFavorites_Click(object sender, RoutedEventArgs e) =>
        PageNavigation.NavigateTo(this, "all-tools", new SectionNavigationRequest("Favorites"));

    private void RunCheckupButton_Click(object sender, RoutedEventArgs e) => NavigateTo("checkup");
    private void ViewActivityButton_Click(object sender, RoutedEventArgs e) => NavigateTo("activity");
    private void OpenDownloads_Click(object sender, RoutedEventArgs e) => NavigateTo("downloads");
    private void OpenFiles_Click(object sender, RoutedEventArgs e) => NavigateTo("storage-browser");
    private void OpenProcesses_Click(object sender, RoutedEventArgs e) => NavigateTo("processes");
    private void OpenCleanup_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateToSection(this, "system-care", "Clean up");
    private void OpenStartup_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateToSection(this, "system-care", "Apps & startup");
    private void OpenNetwork_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateToSection(this, "system-care", "Network & updates");
    private void NavCategory_System_Click(object sender, RoutedEventArgs e) => NavigateTo("checkup");
    private void NavCategory_Security_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateToSection(this, "security", "Status");
    private void NavCategory_Storage_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateToSection(this, "system-care", "Clean up");
    private void NavCategory_Updates_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateToSection(this, "system-care", "Network & updates");
    private void NavigateTo(string key) => PageNavigation.NavigateTo(this, key);

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < HomeCompactBreakpointDip;
        ViewModel.SetCompactLayout(compact);
        PageLayout.Padding = compact ? new Thickness(20, 20, 20, 28) : new Thickness(32, 28, 32, 36);
        HeroLayout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        HeroLayout.ColumnDefinitions[1].Width = compact ? new GridLength(0) : GridLength.Auto;
        HeroLayout.ColumnDefinitions[2].Width = compact ? new GridLength(0) : GridLength.Auto;
        // Compact stacks the coverage stat below the masthead instead of hiding it,
        // with the vertical hairline dropped because it no longer divides columns.
        HeroDivider.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(HeroStats, compact ? 0 : 2);
        Grid.SetRow(HeroStats, compact ? 1 : 0);
        HeroStats.MinWidth = compact ? 0 : 170;
        RecommendationsGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        RecommendationsGrid.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(CleanupRecommendation, 0); Grid.SetRow(CleanupRecommendation, 0);
        Grid.SetColumn(StartupRecommendation, compact ? 0 : 1); Grid.SetRow(StartupRecommendation, compact ? 1 : 0);
        Grid.SetColumn(NetworkRecommendation, 0); Grid.SetRow(NetworkRecommendation, compact ? 2 : 1);
        Grid.SetColumn(DownloadsRecommendation, compact ? 0 : 1); Grid.SetRow(DownloadsRecommendation, compact ? 3 : 1);
        Grid.SetColumn(FilesRecommendation, 0); Grid.SetRow(FilesRecommendation, compact ? 4 : 2);
        Grid.SetColumn(ProcessesRecommendation, compact ? 0 : 1); Grid.SetRow(ProcessesRecommendation, compact ? 5 : 2);
        OverviewGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        OverviewGrid.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(RecentActivityCard, compact ? 0 : 1); Grid.SetRow(RecentActivityCard, compact ? 1 : 0);
    }
}

public sealed class HomeFavoriteTool
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public bool Available { get; set; }
    public string AccessibleName => $"{Title}, {Subtitle}. Open tool details";
}
