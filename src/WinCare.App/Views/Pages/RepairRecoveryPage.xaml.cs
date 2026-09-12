using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCare.App.Views;
using WinCare.App.ViewModels.Pages;

namespace WinCare.App.Views.Pages;

public sealed partial class RepairRecoveryPage : Page
{
    public RepairRecoveryPage()
    {
        ViewModel = new RepairRecoveryPageViewModel();
        InitializeComponent();
        SectionSelector.SelectedItem = SectionSelector.Items[0] as SelectorBarItem;
    }

    public RepairRecoveryPageViewModel ViewModel { get; }

    private void SectionSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ViewModel.SelectSection(sender.Items.IndexOf(sender.SelectedItem));
        if (!ViewModel.IsPlaybookSection)
            ViewModel.ShowTools(WinCare.App.Services.AppRuntime.Current.ToolCatalog, ViewModel.ToolSearchQuery, WinCare.App.Services.AppRuntime.Current.Journal);
    }

    private void PlaybookStep_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PageRow { CommandId: { } id }) PageNavigation.OpenTools(this, id);
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.ShowTools(WinCare.App.Services.AppRuntime.Current.ToolCatalog, ViewModel.ToolSearchQuery, WinCare.App.Services.AppRuntime.Current.Journal);
    }

}
