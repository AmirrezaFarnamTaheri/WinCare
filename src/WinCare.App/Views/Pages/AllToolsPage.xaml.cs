using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinCare.App.ViewModels.Pages;

namespace WinCare.App.Views.Pages;

public sealed partial class AllToolsPage : Page
{
    public AllToolsPage()
    {
        ViewModel = new AllToolsPageViewModel();
        InitializeComponent();
        ToolTabs.SelectedItem = ToolTabs.Items[0] as SelectorBarItem;
    }

    public AllToolsPageViewModel ViewModel { get; }
    public bool IsCompact => ViewModel.IsCompactLayout;

    public static Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility InvertBoolToVisibility(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string query && !string.IsNullOrWhiteSpace(query))
        {
            ViewModel.SearchText = query;
            ToolSearchBox.Focus(FocusState.Programmatic);
        }
    }

    private void ToolTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is SelectorBarItem item)
        {
            ViewModel.SelectTab(item.Text);
        }
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleFavorite();
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = LayoutVisibility.IsCompact(e.NewSize.Width);
        ViewModel.SetCompactLayout(compact);
        DetailsSplitView.DisplayMode = compact ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;
    }
}

