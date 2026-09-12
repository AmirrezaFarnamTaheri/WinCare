using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WinCare.App.ViewModels.Pages;

public abstract class TabbedPageViewModel : ObservableObject
{
    private int _selectedIndex;
    private bool _isCompactLayout;
    private WinCare.Application.Tools.ToolCatalogService? _careCatalog;
    private string _careQuery = string.Empty;
    private WinCare.Application.Activity.IActivityJournalService? _careJournal;

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

    public void ShowTools(WinCare.Application.Tools.ToolCatalogService catalog, string query,
        WinCare.Application.Activity.IActivityJournalService? journal = null)
    {
        _careCatalog = catalog;
        _careQuery = query;
        _careJournal = journal;
        CurrentRows.Clear();
        var matches = WinCare.Application.Tools.CareAreaProjectionService.Project(catalog, query, journal?.GetAll() ?? []);
        foreach (var projection in matches)
        {
            var tool = new ToolRowViewModel(projection.Command);
            CurrentRows.Add(new PageRow(tool.Title, tool.Summary, tool.StatusPillLabel,
                $"{tool.Risk} · Administrator: {tool.AdministratorAccess}\nRestart: {tool.Restart}")
            {
                CommandId = tool.Id,
                IsCompact = IsCompactLayout,
                LatestActivity = projection.LatestActivity is { } activity
                    ? $"Last activity: {(activity.State == WinCare.Domain.Activity.ActivityState.NeedsAttention ? "Needs attention" : activity.State.ToString())} · {activity.StartedAt.ToLocalTime():g}" : string.Empty,
            });
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void RefreshTools()
    {
        if (_careCatalog is not null) ShowTools(_careCatalog, _careQuery, _careJournal);
    }
}
