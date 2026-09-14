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

    /// <summary>Handles the section selector selection changed event.</summary>
    private void SectionSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ViewModel.SelectSection(sender.Items.IndexOf(sender.SelectedItem));
        ViewModel.ShowTools(WinCare.App.Services.AppRuntime.Current.ToolCatalog, ViewModel.ToolSelection, WinCare.App.Services.AppRuntime.Current.Journal);
    }

    /// <summary>Handles navigation to the page.</summary>
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        int sectionIndex = PageNavigation.ResolveSectionIndex(SectionSelector, e.Parameter);
        if (sectionIndex >= 0)
        {
            SectionSelector.SelectedItem = SectionSelector.Items[sectionIndex] as SelectorBarItem;
            ViewModel.SelectSection(sectionIndex);
        }
        ViewModel.ShowTools(WinCare.App.Services.AppRuntime.Current.ToolCatalog, ViewModel.ToolSelection, WinCare.App.Services.AppRuntime.Current.Journal);
    }
}
