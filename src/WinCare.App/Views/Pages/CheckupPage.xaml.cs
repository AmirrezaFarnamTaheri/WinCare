using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinCare.App.ViewModels.Pages;
using WinCare.App.Views;

namespace WinCare.App.Views.Pages;

public sealed partial class CheckupPage : Page
{
    public CheckupPage()
    {
        ViewModel = new CheckupPageViewModel();
        InitializeComponent();
        SectionSelector.SelectedItem = SectionSelector.Items[0] as SelectorBarItem;
    }

    public CheckupPageViewModel ViewModel { get; }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        // The page is cached: without this the awaited probe results land on a live object
        // graph and rewrite rows the user is no longer looking at.
        ViewModel.CancelRunningCheck();
        base.OnNavigatedFrom(e);
    }

    private void SectionSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
        ViewModel.SelectSection(sender.Items.IndexOf(sender.SelectedItem));

    private void FindingAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PageRow row } ||
            string.IsNullOrWhiteSpace(row.NavigationKey) ||
            string.IsNullOrWhiteSpace(row.NavigationSectionTitle)) return;

        PageNavigation.NavigateToSection(this, row.NavigationKey, row.NavigationSectionTitle);
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = LayoutVisibility.IsCompact(e.NewSize.Width);
        ViewModel.SetCompactLayout(compact);
        CheckupHero.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        CheckupHero.ColumnDefinitions[1].Width = compact ? new GridLength(0) : GridLength.Auto;
        Grid.SetRow(CheckupStatusCard, compact ? 1 : 0);
        Grid.SetColumn(CheckupStatusCard, compact ? 0 : 1);
        CheckupStatusCard.HorizontalAlignment = compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
    }
}
