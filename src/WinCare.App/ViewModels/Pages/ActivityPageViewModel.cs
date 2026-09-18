using WinCare.Application.Activity;
using WinCare.App.Services;
using WinCare.Domain.Activity;

namespace WinCare.App.ViewModels.Pages;

public sealed class ActivityPageViewModel : TabbedPageViewModel
{
    private readonly ActivityJournalService _journal;
    private IReadOnlyList<ActivityRecord>? _lastRecords;
    private bool? _lastPersistenceHealthy;
    private string? _lastPersistenceMessage;

    private readonly List<PageRow> _runningRows = [];
    private readonly List<PageRow> _attentionRows = [];
    private readonly List<PageRow> _historyRows = [];
    private readonly List<PageRow> _reportRows = [];

    private const int RunningIndex = 0;
    private const int NeedsAttentionIndex = 1;
    private const int HistoryIndex = 2;
    private const int ReportsIndex = 3;

    public ActivityPageViewModel() : this(AppRuntime.Current.Journal) { }

    public ActivityPageViewModel(ActivityJournalService journal)
        : base([
            new PageSection("Running", "Nothing is running right now.", []),
            new PageSection("Needs attention", "Nothing needs your attention.", []),
            new PageSection("History", "No finished activity yet.", []),
            new PageSection("Reports", "Reports appear after WinCare has some activity to summarize.", [])])
    {
        _journal = journal;
        RefreshFromJournal();
    }

    public event EventHandler? JournalChanged
    {
        add => _journal.Changed += value;
        remove => _journal.Changed -= value;
    }

    public bool HasAttentionItems => _journal.GetAll().Any(r => r.State == ActivityState.NeedsAttention);
    public bool HasPersistenceWarning => !_journal.IsPersistenceHealthy;
    public string PersistenceWarningMessage => _journal.PersistenceStatusMessage ?? "WinCare can't save activity history right now.";

    public void RefreshFromJournal()
    {
        IReadOnlyList<ActivityRecord> records = _journal.GetAll();
        bool persistenceHealthy = _journal.IsPersistenceHealthy;
        string? persistenceMessage = _journal.PersistenceStatusMessage;
        bool recordsChanged = _lastRecords is null || !_lastRecords.SequenceEqual(records);
        bool persistenceChanged = _lastPersistenceHealthy != persistenceHealthy ||
            !string.Equals(_lastPersistenceMessage, persistenceMessage, StringComparison.Ordinal);

        if (!recordsChanged && !persistenceChanged) return;

        _lastRecords = records;
        _lastPersistenceHealthy = persistenceHealthy;
        _lastPersistenceMessage = persistenceMessage;

        if (recordsChanged)
        {
            _runningRows.Clear();
            _attentionRows.Clear();
            _historyRows.Clear();
            _reportRows.Clear();

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
                        _historyRows.Add(row);
                        break;
                }
            }

            BuildDailyReports(records);
            RefreshCurrentRows();
            OnPropertyChanged(nameof(HasAttentionItems));
        }

        if (persistenceChanged)
        {
            OnPropertyChanged(nameof(HasPersistenceWarning));
            OnPropertyChanged(nameof(PersistenceWarningMessage));
        }
    }

    public override void SelectSection(int index)
    {
        base.SelectSection(index);
        RefreshCurrentRows();
    }

    private void RefreshCurrentRows()
    {
        IReadOnlyList<PageRow> rows = SelectedIndex switch
        {
            RunningIndex => _runningRows,
            NeedsAttentionIndex => _attentionRows,
            HistoryIndex => _historyRows,
            ReportsIndex => _reportRows,
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

    private void BuildDailyReports(IReadOnlyList<ActivityRecord> records)
    {
        var terminal = records
            .Where(record => record.State is ActivityState.Completed or ActivityState.Failed or ActivityState.Cancelled)
            .GroupBy(record => record.CompletedAt?.ToLocalTime().Date ?? record.StartedAt.ToLocalTime().Date)
            .OrderByDescending(group => group.Key);

        foreach (var day in terminal)
        {
            ActivityRecord[] entries = day.OrderBy(record => record.StartedAt).ToArray();
            int succeeded = entries.Count(record => record.State == ActivityState.Completed);
            int failed = entries.Count(record => record.State == ActivityState.Failed);
            int cancelled = entries.Count(record => record.State == ActivityState.Cancelled);
            string state = failed > 0 ? "Review" : cancelled > 0 ? "Stopped" : "Complete";
            string description = $"{entries.Length} operations · {succeeded} completed · {failed} failed · {cancelled} cancelled";

            if (records.Count >= ActivityJournalService.MaxPersistedRecords)
                description += $" · only the latest {ActivityJournalService.MaxPersistedRecords} records are kept";

            string first = entries[0].StartedAt.ToLocalTime().ToString("HH:mm");
            string last = entries.Max(record => record.CompletedAt ?? record.StartedAt).ToLocalTime().ToString("HH:mm");
            _reportRows.Add(new PageRow(day.Key.ToString("dddd, MMM d, yyyy"), description, state, $"{first}-{last}"));
        }
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
            ? rec.CompletedAt.Value.ToLocalTime().ToString("g")
            : $"Started {rec.StartedAt.ToLocalTime():g}";

        return new PageRow(rec.Title, rec.Result, state, detail);
    }
}
