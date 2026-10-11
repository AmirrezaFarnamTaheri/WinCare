using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinCare.App.Views.Pages;

public sealed partial class HelpPage : Page
{
    public HelpPage() => InitializeComponent();

    private void OpenCheckup_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "checkup");
    private void OpenActivity_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "activity");
    private void OpenFiles_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "storage-browser");
    private void OpenProcesses_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "processes");
    private void OpenDownloads_Click(object sender, RoutedEventArgs e) => PageNavigation.NavigateTo(this, "downloads");
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
    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < 680;
        HelpLayout.Padding = compact ? new Thickness(16,18,16,24) : new Thickness(32,28,32,28);
        EverydayActions.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        EverydayActions.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        EverydayActions.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Button[] actions = [BrowseStorageButton, ViewProcessesButton, ManageDownloadsButton];
        for (int i = 0; i < actions.Length; i++)
        {
            Grid.SetRow(actions[i], compact ? i : 0);
            Grid.SetColumn(actions[i], compact ? 0 : i);
        }
    }


}
