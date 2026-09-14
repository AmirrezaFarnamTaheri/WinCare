using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinCare.App.Views.Pages;

public sealed partial class HelpPage : Page
{
    public HelpPage() => InitializeComponent();

    private void OpenCheckup_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "checkup");
    /// <summary>Handles the open power tools click event.</summary>
    private void OpenPowerTools_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "all-tools");
    private void OpenActivity_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "activity");
    private void OpenPluginStore_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "plugin-store");
    /// <summary>Handles the open troubleshoot click event.</summary>
    private void OpenTroubleshoot_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "ai-doctor");
    private void OpenSettings_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "settings");
    /// <summary>Handles the open about click event.</summary>
    private void OpenAbout_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "about");
    /// <summary>Handles the show tour click event.</summary>
    private async void ShowTour_Click(object sender, RoutedEventArgs e) => await PageNavigation.ShowTourAsync(this);
}
