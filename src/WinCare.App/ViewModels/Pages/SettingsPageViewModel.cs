using CommunityToolkit.Mvvm.ComponentModel;
using WinCare.App.Services;

namespace WinCare.App.ViewModels.Pages;

public sealed class SettingsPageViewModel : ObservableObject
{
    private string _selectedTheme = AppPreferences.Theme;

    public IReadOnlyList<string> Themes { get; } = ["System", "Light", "Dark"];

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            string normalized = value is "Light" or "Dark" ? value : "System";
            if (SetProperty(ref _selectedTheme, normalized)) AppPreferences.Theme = normalized;
        }
    }

    public string DataDirectory => AppPreferences.DataDirectory;
}
