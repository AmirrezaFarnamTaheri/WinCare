using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json;

namespace WinCare.App.ViewModels.Pages;

public sealed class PageRow : ObservableObject
{
    private bool _isCompact;
    private string _state;
    private string _detail;
    private string _statusBrushKey = "AccentTealBrush";
    private string? _actionText;
    private CommunityToolkit.Mvvm.Input.IRelayCommand? _actionCommand;
    private string? _navigationKey;
    private int? _navigationSectionIndex;

    public PageRow(string title, string description, string state, string detail)
    {
        Title = title;
        Description = description;
        _state = state;
        _detail = detail;
    }

    public string Title { get; }
    public override string ToString() => Title;
    public string? CommandId { get; init; }
    public JsonElement? CommandParameters { get; init; }
    public string LatestActivity { get; init; } = string.Empty;
    public bool HasActivity => LatestActivity.Length > 0;
    public string AccessibleName => $"{Title}. {Description}. {State}. {Detail}";
    public string Description { get; }

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

    public string StatusBrushKey
    {
        get => _statusBrushKey;
        set => SetProperty(ref _statusBrushKey, value);
    }

    public string? ActionText
    {
        get => _actionText;
        set
        {
            if (SetProperty(ref _actionText, value))
            {
                OnPropertyChanged(nameof(HasAction));
            }
        }
    }

    public CommunityToolkit.Mvvm.Input.IRelayCommand? ActionCommand
    {
        get => _actionCommand;
        set
        {
            if (SetProperty(ref _actionCommand, value))
            {
                OnPropertyChanged(nameof(HasAction));
            }
        }
    }

    public string? NavigationKey
    {
        get => _navigationKey;
        set
        {
            if (SetProperty(ref _navigationKey, value))
            {
                OnPropertyChanged(nameof(HasAction));
            }
        }
    }

    public int? NavigationSectionIndex
    {
        get => _navigationSectionIndex;
        set => SetProperty(ref _navigationSectionIndex, value);
    }

    public bool HasAction => !string.IsNullOrWhiteSpace(ActionText) &&
        (ActionCommand is not null || !string.IsNullOrWhiteSpace(NavigationKey));

    public bool IsCompact
    {
        get => _isCompact;
        set => SetProperty(ref _isCompact, value);
    }
}
