using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinCare.App.Views.Pages;

public sealed partial class HelpPage : Page
{
    public HelpPage() => InitializeComponent();

    private void OpenCheckup_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "checkup");
    private void OpenActivity_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "activity");
    private void OpenPluginStore_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "plugin-store");
    private void OpenSettings_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "settings");
}
