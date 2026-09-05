using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCare.App.Views;
using WinCare.App.ViewModels.Pages;

namespace WinCare.App.Views.Pages;

public sealed partial class SecurityPage : Page
{
    public SecurityPage()
    {
        ViewModel = new SecurityPageViewModel();
        InitializeComponent();
        SectionSelector.SelectedItem = SectionSelector.Items[0] as SelectorBarItem;
    }

    public SecurityPageViewModel ViewModel { get; }

    private void OpenToolsButton_Click(object sender, RoutedEventArgs e) =>
        PageNavigation.OpenTools(this, ViewModel.ToolSearchQuery);

    private void SectionSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ViewModel.SelectSection(sender.Items.IndexOf(sender.SelectedItem));
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = LayoutVisibility.IsCompact(e.NewSize.Width);
        ViewModel.SetCompactLayout(compact);
        DescriptionHeader.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        StateHeader.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        NotesHeader.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }
}
