using CommunityToolkit.Mvvm.ComponentModel;

namespace WinCare.App.ViewModels.Pages;

public sealed class PageRow : ObservableObject
{
    private bool _isCompact;

    public PageRow(string title, string description, string state, string detail)
    {
        Title = title;
        Description = description;
        State = state;
        Detail = detail;
    }

    public string Title { get; }
    public string Description { get; }
    public string State { get; }
    public string Detail { get; }

    public bool IsCompact
    {
        get => _isCompact;
        set => SetProperty(ref _isCompact, value);
    }
}
