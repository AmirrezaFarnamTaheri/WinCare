using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCare.App.ViewModels.Pages;

namespace WinCare.App.Views.Pages;

public sealed partial class SettingsPage : Page
{
    private const double CompactThreshold = 820;

    public SettingsPage()
    {
        ViewModel = new SettingsPageViewModel();
        InitializeComponent();
        SectionSelector.SelectedItem = SectionSelector.Items[0] as SelectorBarItem;
    }

    public SettingsPageViewModel ViewModel { get; }

    private void SectionSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ViewModel.SelectSection(sender.Items.IndexOf(sender.SelectedItem));
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < CompactThreshold;
        ViewModel.SetCompactLayout(compact);
        DescriptionHeader.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        StateHeader.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        NotesHeader.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }
}
