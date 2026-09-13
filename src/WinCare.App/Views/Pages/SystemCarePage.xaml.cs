using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCare.App.Views;
using WinCare.App.ViewModels.Pages;

namespace WinCare.App.Views.Pages;

public sealed partial class SystemCarePage : Page
{
    public SystemCarePage()
    {
        ViewModel = new SystemCarePageViewModel();
        InitializeComponent();
        SectionSelector.SelectedItem = SectionSelector.Items[0] as SelectorBarItem;
    }

    public SystemCarePageViewModel ViewModel { get; }

    private void SectionSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ViewModel.SelectSection(sender.Items.IndexOf(sender.SelectedItem));
        ViewModel.ShowTools(WinCare.App.Services.AppRuntime.Current.ToolCatalog, ViewModel.ToolSearchQuery, WinCare.App.Services.AppRuntime.Current.Journal);
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.ShowTools(WinCare.App.Services.AppRuntime.Current.ToolCatalog, ViewModel.ToolSearchQuery, WinCare.App.Services.AppRuntime.Current.Journal);
    }

}
