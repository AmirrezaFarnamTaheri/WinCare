using WinCare.Domain.Activity;

namespace WinCare.Application.Activity;

/// <summary>
/// Thread-safe, append-only in-process journal for command activity records.
/// </summary>
public sealed class ActivityJournalService : IActivityJournalService
{
    private readonly List<ActivityRecord> _records = [];
    private readonly object _lock = new();

    /// <summary>Returns a snapshot of all records. Safe to call from any thread.</summary>
    public IReadOnlyList<ActivityRecord> GetAll()
    {
        lock (_lock)
        {
            return _records.ToArray();
        }
    }

    /// <summary>Begins a new activity record in the Running state.</summary>
    public ActivityRecord Begin(string commandId, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var record = new ActivityRecord(
            Guid.NewGuid(),
            commandId,
            title,
            ActivityState.Running,
            DateTimeOffset.UtcNow,
            CompletedAt: null,
            Result: string.Empty,
            UndoAvailable: false);
        lock (_lock)
        {
            _records.Add(record);
        }
        return record;
    }

    /// <summary>Transitions the record to Completed.</summary>
    public void Complete(Guid id, string result, bool undoAvailable = false)
        => Update(id, ActivityState.Completed, result, undoAvailable);

    /// <summary>Transitions the record to Failed.</summary>
    public void Fail(Guid id, string result)
        => Update(id, ActivityState.Failed, result, undoAvailable: false);

    /// <summary>Transitions the record to Cancelled.</summary>
    public void Cancel(Guid id)
        => Update(id, ActivityState.Cancelled, "Cancelled by user.", undoAvailable: false);

    /// <summary>Transitions the record to NeedsAttention with a diagnostic message.</summary>
    public void RequireAttention(Guid id, string message)
        => Update(id, ActivityState.NeedsAttention, message, undoAvailable: false);

    private void Update(Guid id, ActivityState state, string result, bool undoAvailable)
    {
        lock (_lock)
        {
            int i = _records.FindIndex(r => r.Id == id);
            if (i < 0)
            {
                return;
            }
            _records[i] = _records[i] with
            {
                State = state,
                CompletedAt = DateTimeOffset.UtcNow,
                Result = result,
                UndoAvailable = undoAvailable,
            };
        }
    }
}
