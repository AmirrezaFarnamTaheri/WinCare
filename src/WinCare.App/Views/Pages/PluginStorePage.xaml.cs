namespace WinCare.App.Views.Pages;

using Microsoft.UI.Xaml.Controls;
using WinCare.App.ViewModels.Pages;

public sealed partial class PluginStorePage : Page
{
    public PluginStorePage()
    {
        ViewModel = new PluginStorePageViewModel();
        InitializeComponent();
    }

    public PluginStorePageViewModel ViewModel { get; }
}
