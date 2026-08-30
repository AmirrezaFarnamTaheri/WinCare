using CommunityToolkit.Mvvm.ComponentModel;

namespace WinCare.App.ViewModels.Pages;

public sealed class PageRow : ObservableObject
{
    private bool _isCompact;

    public PageRow(string title, string description, string state, string detail)
    {
        Title = title;
        Description = description;
        _state = state;
        _detail = detail;
    }

    public string Title { get; }
    public string Description { get; }
    private string _state;
    private string _detail;

    public string State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    public string Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }

    public bool IsCompact
    {
        get => _isCompact;
        set => SetProperty(ref _isCompact, value);
    }
}
