using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCare.App.ViewModels.Pages;

namespace WinCare.App.Views.Pages;

public sealed partial class ActivityPage : Page
{
    public ActivityPage()
    {
        ViewModel = new ActivityPageViewModel();
        InitializeComponent();
        SectionSelector.SelectedItem = SectionSelector.Items[0] as SelectorBarItem;
    }

    public ActivityPageViewModel ViewModel { get; }
    public bool IsCompact => ViewModel.IsCompactLayout;

    private void SectionSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is SelectorBarItem item)
        {
            int index = sender.Items.IndexOf(item);
            if (index >= 0)
            {
                ViewModel.SelectSection(index);
            }
        }
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = LayoutVisibility.IsCompact(e.NewSize.Width);
        ViewModel.SetCompactLayout(compact);
    }
}

