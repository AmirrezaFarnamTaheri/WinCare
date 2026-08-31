using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCare.App.Services;
using WinCare.App.ViewModels.Pages;

namespace WinCare.App.Views.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = new SettingsPageViewModel();
        InitializeComponent();
    }

    public SettingsPageViewModel ViewModel { get; }

    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Microsoft.UI.Xaml.Application.Current is App app && app.MainWindow is not null)
        {
            app.MainWindow.ApplyTheme(ViewModel.SelectedTheme);
        }
    }

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPreferences.DataDirectory);
        Process.Start(new ProcessStartInfo { FileName = AppPreferences.DataDirectory, UseShellExecute = true });
    }
}
