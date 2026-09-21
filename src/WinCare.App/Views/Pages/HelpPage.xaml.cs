using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinCare.App.Views.Pages;

public sealed partial class HelpPage : Page
{
    public HelpPage() => InitializeComponent();

    private void OpenCheckup_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "checkup");
    private void OpenActivity_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "activity");
    private void OpenSettings_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "settings");
    private void OpenAbout_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "about");
    private async void ShowTour_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            TourError.IsOpen = false;
            await PageNavigation.ShowTourAsync(this);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HelpPage] Tour failed: {ex}");
            TourError.IsOpen = true;
        }
    }
}
