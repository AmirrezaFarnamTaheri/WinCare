using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WinCare.Application.Activity;
using WinCare.Application.Tools;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Activity;

namespace WinCare.App.ViewModels.Pages;

public abstract class TabbedPageViewModel : ObservableObject
{
    private int _selectedIndex;
    private bool _isCompactLayout;
    private ToolCatalogService? _careCatalog;
    private CareAreaSelection? _careSelection;
    private string? _careQuery;
    private IActivityJournalService? _careJournal;

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

    /// <summary>
    /// Shows a deterministic product section using the catalog's Area/Section taxonomy.
    /// </summary>
    public void ShowTools(
        ToolCatalogService catalog,
        CareAreaSelection selection,
        IActivityJournalService? journal = null)
    {
        _careCatalog = catalog;
        _careSelection = selection;
        _careQuery = null;
        _careJournal = journal;
        Populate(CareAreaProjectionService.Project(catalog, selection, journal?.GetAll() ?? []));
    }

    /// <summary>
    /// Command search projection retained for command-oriented surfaces and tests.
    /// Care pages should prefer the structured overload.
    /// </summary>
    public void ShowTools(ToolCatalogService catalog, string query, IActivityJournalService? journal = null)
    {
        _careCatalog = catalog;
        _careSelection = null;
        _careQuery = query;
        _careJournal = journal;
        Populate(CareAreaProjectionService.Project(catalog, query, journal?.GetAll() ?? []));
    }

    public void RefreshTools()
    {
        if (_careCatalog is null) return;
        if (_careSelection is not null)
        {
            ShowTools(_careCatalog, _careSelection, _careJournal);
        }
        else if (_careQuery is not null)
        {
            ShowTools(_careCatalog, _careQuery, _careJournal);
        }
    }

    private void Populate(IReadOnlyList<CareToolProjection> matches)
    {
        CurrentRows.Clear();
        foreach (CareToolProjection projection in matches)
        {
            var tool = new ToolRowViewModel(projection.Command);
            string impact = projection.Command.ReadOnly
                ? "Read-only"
                : projection.Command.Risk switch
                {
                    CommandRisk.Low => "Low impact",
                    CommandRisk.Moderate => "Moderate impact",
                    CommandRisk.High => "High impact",
                    CommandRisk.Critical => "Critical impact",
                    _ => "Changes Windows",
                };
            string access = projection.Command.AdministratorAccess switch
            {
                AdministratorAccess.No => "Standard access",
                AdministratorAccess.MayBeRequired => "Administrator may be needed",
                AdministratorAccess.Required => "Administrator required",
                _ => "Access varies",
            };
            string restart = projection.Command.Restart switch
            {
                RestartExpectation.No => "No restart expected",
                RestartExpectation.MayBeRequired => "Restart may be needed",
                RestartExpectation.Required => "Restart required",
                _ => "Restart varies",
            };

            CurrentRows.Add(new PageRow(tool.Title, tool.Summary, impact, $"{access} · {restart}")
            {
                CommandId = tool.Id,
                IsCompact = IsCompactLayout,
                LatestActivity = projection.LatestActivity is { } activity
                    ? $"Last activity: {(activity.State == ActivityState.NeedsAttention ? "Needs attention" : activity.State.ToString())} · {activity.StartedAt.ToLocalTime():g}"
                    : string.Empty,
            });
        }
        OnPropertyChanged(nameof(IsEmpty));
    }
}
