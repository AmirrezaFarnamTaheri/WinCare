using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinCare.App.ViewModels.Pages;

namespace WinCare.App.Views.Pages;

public sealed partial class ActivityPage : Page
{
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    public ActivityPage()
    {
        ViewModel = new ActivityPageViewModel();
        InitializeComponent();
        SectionSelector.SelectedItem = SectionSelector.Items[0] as SelectorBarItem;
        _refreshTimer.Tick += (_, _) => ViewModel.RefreshFromJournal();
    }

    public ActivityPageViewModel ViewModel { get; }

    public bool IsCompact => ViewModel.IsCompactLayout;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.RefreshFromJournal();
        _refreshTimer.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _refreshTimer.Stop();
        base.OnNavigatedFrom(e);
    }

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
