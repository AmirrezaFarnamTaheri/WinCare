using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WinCare.App.ViewModels.Pages;

public abstract class TabbedPageViewModel : ObservableObject
{
    private int _selectedIndex;
    private bool _isCompactLayout;

    protected TabbedPageViewModel(IReadOnlyList<PageSection> sections)
    {
        Sections = sections ?? throw new ArgumentNullException(nameof(sections));
        if (Sections.Count == 0)
        {
            throw new ArgumentException("At least one section is required.", nameof(sections));
        }
        CurrentRows = new ObservableCollection<PageRow>(Sections[0].Rows);
    }

    public IReadOnlyList<PageSection> Sections { get; }
    public ObservableCollection<PageRow> CurrentRows { get; }

    public int SelectedIndex
    {
        get => _selectedIndex;
        private set => SetProperty(ref _selectedIndex, value);
    }

    public bool IsCompactLayout
    {
        get => _isCompactLayout;
        private set => SetProperty(ref _isCompactLayout, value);
    }

    public bool IsEmpty => CurrentRows.Count == 0;
    public string EmptyMessage => Sections[SelectedIndex].EmptyMessage;

    public virtual void SelectSection(int index)
    {
        if (index < 0 || index >= Sections.Count || index == SelectedIndex)
        {
            return;
        }

        SelectedIndex = index;
        CurrentRows.Clear();
        foreach (PageRow row in Sections[index].Rows)
        {
            row.IsCompact = IsCompactLayout;
            CurrentRows.Add(row);
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    public void SetCompactLayout(bool isCompact)
    {
        if (IsCompactLayout == isCompact)
        {
            return;
        }

        IsCompactLayout = isCompact;
        foreach (PageRow row in CurrentRows)
        {
            row.IsCompact = isCompact;
        }
    }
}
