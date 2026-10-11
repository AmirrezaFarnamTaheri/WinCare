using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
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

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.PersistenceStatusChanged += Preferences_PersistenceStatusChanged;
        ViewModel.RefreshPersistenceState();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.PersistenceStatusChanged -= Preferences_PersistenceStatusChanged;
        base.OnNavigatedFrom(e);
    }

    private void Preferences_PersistenceStatusChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(ViewModel.RefreshPersistenceState);
    }

    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Microsoft.UI.Xaml.Application.Current is App app && app.MainWindow is not null)
        {
            app.MainWindow.ApplyTheme(ViewModel.SelectedTheme);
        }
    }

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPreferences.DataDirectory);
            Process.Start(new ProcessStartInfo { FileName = AppPreferences.DataDirectory, UseShellExecute = true });
            FolderErrorInfoBar.IsOpen = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsPage] Open data folder failed: {ex}");
            FolderErrorInfoBar.Message = "WinCare could not open the local data folder. You can copy the path shown above and open it manually.";
            FolderErrorInfoBar.IsOpen = true;
        }
    }
    private void CopyDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var contents = new DataPackage();
            contents.SetText(ViewModel.DataDirectory);
            Clipboard.SetContent(contents);
            FolderErrorInfoBar.IsOpen = false;
            CopiedPathInfoBar.IsOpen = true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            CopiedPathInfoBar.IsOpen = false;
            FolderErrorInfoBar.Message = "The clipboard is busy. Select the folder path above to copy it manually.";
            FolderErrorInfoBar.IsOpen = true;
        }
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < 680;
        SettingsLayout.Padding = compact ? new Thickness(16,18,16,24) : new Thickness(32,28,32,32);
        Grid.SetRow(ThemeSelector, compact ? 1 : 0);
        Grid.SetColumn(ThemeSelector, compact ? 0 : 1);
        Grid.SetRow(WindowPlacementSwitch, compact ? 1 : 0);
        Grid.SetColumn(WindowPlacementSwitch, compact ? 0 : 1);
        ThemeSelector.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        WindowPlacementSwitch.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }


}
