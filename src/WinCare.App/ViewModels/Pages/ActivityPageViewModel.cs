using System.Collections.ObjectModel;
using WinCare.Application.Activity;
using WinCare.Application.Commands;
using WinCare.App.Services;
using WinCare.Domain.Activity;

namespace WinCare.App.ViewModels.Pages;

public sealed class ActivityPageViewModel : TabbedPageViewModel
{
    private readonly ActivityJournalService _journal;

    // Backing mutable row lists — one per section, in section order.
    private readonly List<PageRow> _runningRows = [];
    private readonly List<PageRow> _attentionRows = [];
    private readonly List<PageRow> _completedRows = [];

    // Section indices must match the constructor order below.
    private const int RunningIndex = 0;
    private const int NeedsAttentionIndex = 1;
    private const int CompletedIndex = 2;
    private const int ReportsIndex = 3;

    public ActivityPageViewModel()
        : this(AppRuntime.Current.Journal)
    {
    }

    public ActivityPageViewModel(ActivityJournalService journal)
        : base([
            new PageSection("Running", "No operations are running.", []),
            new PageSection("Needs attention", "No operations need attention.", []),
            new PageSection("Completed", "Completed native operations will appear here.", []),
            new PageSection("Reports", "No native reports are available.", [])])
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        RefreshFromJournal();
    }

    public bool HasAttentionItems =>
        _journal.GetAll().Any(r => r.State == ActivityState.NeedsAttention);

    /// <summary>
    /// Rebuilds all section rows from the current journal state and refreshes the view.
    /// </summary>
    public void RefreshFromJournal()
    {
        IReadOnlyList<ActivityRecord> records = _journal.GetAll();

        _runningRows.Clear();
        _attentionRows.Clear();
        _completedRows.Clear();

        foreach (ActivityRecord rec in records)
        {
            PageRow row = MapToPageRow(rec);
            switch (rec.State)
            {
                case ActivityState.Running:
                    _runningRows.Add(row);
                    break;
                case ActivityState.NeedsAttention:
                    _attentionRows.Add(row);
                    break;
                case ActivityState.Completed:
                case ActivityState.Failed:
                case ActivityState.Cancelled:
                    _completedRows.Add(row);
                    break;
            }
        }

        // Repopulate CurrentRows for the active section.
        RefreshCurrentRows();
        OnPropertyChanged(nameof(HasAttentionItems));
    }

    /// <inheritdoc />
    public override void SelectSection(int index)
    {
        // Let the base class update SelectedIndex first.
        base.SelectSection(index);
        // Then repopulate CurrentRows from our backing lists (base populates from
        // the always-empty Sections[].Rows that were constructed with [].)
        RefreshCurrentRows();
    }

    private void RefreshCurrentRows()
    {
        IReadOnlyList<PageRow> rows = SelectedIndex switch
        {
            RunningIndex => _runningRows,
            NeedsAttentionIndex => _attentionRows,
            CompletedIndex => _completedRows,
            _ => [],
        };

        CurrentRows.Clear();
        foreach (PageRow row in rows)
        {
            row.IsCompact = IsCompactLayout;
            CurrentRows.Add(row);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private static PageRow MapToPageRow(ActivityRecord rec)
    {
        string state = rec.State switch
        {
            ActivityState.Running => "Running",
            ActivityState.NeedsAttention => "Needs attention",
            ActivityState.Completed => "Completed",
            ActivityState.Failed => "Failed",
            ActivityState.Cancelled => "Cancelled",
            _ => rec.State.ToString(),
        };

        string detail = rec.CompletedAt.HasValue
            ? $"{rec.CompletedAt.Value:HH:mm:ss}{(rec.UndoAvailable ? " · Undo available" : string.Empty)}"
            : $"Started {rec.StartedAt:HH:mm:ss}";

        return new PageRow(rec.Title, rec.Result, state, detail);
    }
}
