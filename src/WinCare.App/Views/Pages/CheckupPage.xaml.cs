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

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewModel.SetCompactLayout(LayoutVisibility.IsCompact(e.NewSize.Width));
    }
}
