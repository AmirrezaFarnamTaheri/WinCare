using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCare.App.ViewModels.Pages;

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

    private void SectionSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ViewModel.SelectSection(sender.Items.IndexOf(sender.SelectedItem));
    }

    private void FindingAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PageRow row } && !string.IsNullOrWhiteSpace(row.NavigationKey))
        {
            PageNavigation.NavigateTo(this, row.NavigationKey, row.NavigationSectionIndex);
        }
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = LayoutVisibility.IsCompact(e.NewSize.Width);
        ViewModel.SetCompactLayout(compact);
        if (compact)
        {
            CheckupHero.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            CheckupHero.ColumnDefinitions[1].Width = new GridLength(0);
            CheckupStatusCard.Visibility = Visibility.Collapsed;
        }
        else
        {
            CheckupHero.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            CheckupHero.ColumnDefinitions[1].Width = GridLength.Auto;
            CheckupStatusCard.Visibility = Visibility.Visible;
        }
    }
}
