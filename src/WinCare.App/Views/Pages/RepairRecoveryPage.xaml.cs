using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
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
            ViewModel.ShowTools(WinCare.App.Services.AppRuntime.Current.ToolCatalog, ViewModel.ToolSelection, WinCare.App.Services.AppRuntime.Current.Journal);
    }

    private void PlaybookStep_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PageRow { CommandId: { } id, CommandParameters: JsonElement parameters })
            PageNavigation.OpenTool(this, id, parameters);
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is int sectionIndex && sectionIndex >= 0 && sectionIndex < SectionSelector.Items.Count)
        {
            SectionSelector.SelectedItem = SectionSelector.Items[sectionIndex] as SelectorBarItem;
            ViewModel.SelectSection(sectionIndex);
        }
        if (!ViewModel.IsPlaybookSection)
            ViewModel.ShowTools(WinCare.App.Services.AppRuntime.Current.ToolCatalog, ViewModel.ToolSelection, WinCare.App.Services.AppRuntime.Current.Journal);
    }

}
