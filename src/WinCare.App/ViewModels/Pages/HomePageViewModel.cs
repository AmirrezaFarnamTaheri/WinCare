using CommunityToolkit.Mvvm.ComponentModel;

namespace WinCare.App.ViewModels.Pages;

public sealed class HomePageViewModel : ObservableObject
{
    private bool _isCompactLayout;

    public bool IsCompactLayout
    {
        get => _isCompactLayout;
        private set => SetProperty(ref _isCompactLayout, value);
    }

    public void SetCompactLayout(bool isCompact) => IsCompactLayout = isCompact;
}
