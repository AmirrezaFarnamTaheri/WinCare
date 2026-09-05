using CommunityToolkit.Mvvm.ComponentModel;
using WinCare.App.Services;

namespace WinCare.App.ViewModels.Pages;

public sealed class SettingsPageViewModel : ObservableObject
{
    private string _selectedTheme = AppPreferences.Theme;
    private bool _rememberWindowPlacement = AppPreferences.RememberWindowPlacement;

    public IReadOnlyList<string> Themes { get; } = ["System", "Light", "Dark"];

    public event EventHandler? PersistenceStatusChanged
    {
        add => AppPreferences.PersistenceStatusChanged += value;
        remove => AppPreferences.PersistenceStatusChanged -= value;
    }

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            string normalized = value is "Light" or "Dark" ? value : "System";
            if (SetProperty(ref _selectedTheme, normalized)) AppPreferences.Theme = normalized;
        }
    }

    public bool RememberWindowPlacement
    {
        get => _rememberWindowPlacement;
        set
        {
            if (SetProperty(ref _rememberWindowPlacement, value))
                AppPreferences.RememberWindowPlacement = value;
        }
    }

    public string DataDirectory => AppPreferences.DataDirectory;
    public bool HasPersistenceWarning => !AppPreferences.IsPersistenceHealthy;
    public string PersistenceWarningMessage => AppPreferences.PersistenceStatusMessage ??
        "Preferences cannot currently be saved to disk.";

    public void RefreshPersistenceState()
    {
        OnPropertyChanged(nameof(HasPersistenceWarning));
        OnPropertyChanged(nameof(PersistenceWarningMessage));
    }
}
