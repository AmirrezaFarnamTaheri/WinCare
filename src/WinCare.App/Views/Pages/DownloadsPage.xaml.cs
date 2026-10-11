using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinCare.App.ViewModels.Pages;
using WinCare.App.Views;
using WinCare.Application.Downloads;

namespace WinCare.App.Views.Pages;

public sealed partial class DownloadsPage : Page
{
    private const double CompactBreakpoint = 760;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public DownloadsPage()
    {
        ViewModel = new DownloadsPageViewModel();
        InitializeComponent();
        JobFilters.SelectedItem = JobFilters.Items[0] as SelectorBarItem;
        _refreshTimer.Tick += (_, _) => _ = ViewModel.RefreshAsync();
        ApplyLayout(ActualWidth < CompactBreakpoint);
    }

    public DownloadsPageViewModel ViewModel { get; }
    public static Visibility BoolToVisibility(bool value) =>
        LayoutVisibility.BoolToVisibility(value);

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Activate();
        _ = ViewModel.RefreshAsync();
        _refreshTimer.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _refreshTimer.Stop();
        ViewModel.Deactivate();
        base.OnNavigatedFrom(e);
    }

    private async void Add_Click(object sender, RoutedEventArgs e) => await ViewModel.AddAsync();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ViewModel.RefreshAsync();
    private void JobFilters_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is SelectorBarItem item) ViewModel.SetFilter(item.Text);
    }

    private async Task RunActionAsync(object sender, string action)
    {
        if (sender is Button { Tag: DownloadJobRow row })
            await ViewModel.RunActionAsync(row, action);
    }

    private async void Start_Click(object sender, RoutedEventArgs e) => await RunActionAsync(sender, "start");
    private async void Pause_Click(object sender, RoutedEventArgs e) => await RunActionAsync(sender, "pause");
    private async void Resume_Click(object sender, RoutedEventArgs e) => await RunActionAsync(sender, "resume");
    private async void Cancel_Click(object sender, RoutedEventArgs e) => await RunActionAsync(sender, "cancel");
    private async void Remove_Click(object sender, RoutedEventArgs e) => await RunActionAsync(sender, "remove");

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyLayout(e.NewSize.Width < CompactBreakpoint);

    private void ApplyLayout(bool compact)
    {
        PageLayout.Padding = compact ? new Thickness(16, 18, 16, 20) : new Thickness(32, 28, 32, 28);
        AddGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        AddGrid.ColumnDefinitions[1].Width = compact ? new GridLength(0) : GridLength.Auto;
        Grid.SetColumn(AddButton, compact ? 0 : 1);
        Grid.SetRow(AddButton, compact ? 1 : 0);
        AddButton.HorizontalAlignment = compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        for (int i = 0; i < 3; i++)
        {
            MetricsGrid.ColumnDefinitions[i].Width = compact && i > 0 ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        }
        Grid.SetRow(TotalMetric, 0);
        Grid.SetColumn(ActiveMetric, compact ? 0 : 1);
        Grid.SetRow(ActiveMetric, compact ? 1 : 0);
        Grid.SetColumn(FinishedMetric, compact ? 0 : 2);
        Grid.SetRow(FinishedMetric, compact ? 2 : 0);
    }
}
