using System.Text.Json;
using WinCare.Domain.Activity;
using WinCare.Domain.Serialization;

namespace WinCare.Application.Activity;

/// <summary>
/// Thread-safe, append-only journal for command activity records, persisted to disk so the
/// audit trail survives application restarts. Disk writes are serialized outside the record
/// lock so command dispatch never performs filesystem I/O while holding journal state.
/// </summary>
public sealed class ActivityJournalService : IActivityJournalService
{
    private const int MaxPersistedRecords = 200;
    private const long MaxJournalFileBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions PersistenceOptions = new()
    {
        TypeInfoResolver = WinCareDomainJsonContext.Default,
        WriteIndented = true,
    };

    private readonly string _journalFilePath;
    private readonly List<ActivityRecord> _records;
    private readonly object _recordsLock = new();
    private readonly object _persistenceLock = new();
    private Task _persistenceTail = Task.CompletedTask;
    private string? _persistenceStatusMessage;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>
    /// Initializes the journal, restoring any previously persisted records.
    /// </summary>
    public ActivityJournalService(string? journalFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(journalFilePath))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _journalFilePath = Path.Combine(localAppData, "WinCare", "activity.json");
        }
        else
        {
            _journalFilePath = journalFilePath;
        }

        _records = LoadCore(out _persistenceStatusMessage);
    }

    /// <inheritdoc />
    public bool IsPersistenceHealthy
    {
        get
        {
            lock (_persistenceLock)
            {
                return string.IsNullOrWhiteSpace(_persistenceStatusMessage);
            }
        }
    }

    /// <inheritdoc />
    public string? PersistenceStatusMessage
    {
        get
        {
            lock (_persistenceLock)
            {
                return _persistenceStatusMessage;
            }
        }
    }

    /// <summary>Returns a snapshot of all records. Safe to call from any thread.</summary>
    public IReadOnlyList<ActivityRecord> GetAll()
    {
        lock (_recordsLock)
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

        ActivityRecord[] snapshot;
        lock (_recordsLock)
        {
            _records.Add(record);
            TrimCompletedRecords();
            snapshot = SnapshotForPersistence();
        }

        QueueSave(snapshot);
        RaiseChanged();
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

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Task pending;
        lock (_persistenceLock)
        {
            pending = _persistenceTail;
        }

        await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Update(Guid id, ActivityState state, string result, bool undoAvailable)
    {
        ActivityRecord[] snapshot;
        lock (_recordsLock)
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
            TrimCompletedRecords();
            snapshot = SnapshotForPersistence();
        }

        QueueSave(snapshot);
        RaiseChanged();
    }

    private List<ActivityRecord> LoadCore(out string? persistenceStatusMessage)
    {
        persistenceStatusMessage = null;
        try
        {
            if (!File.Exists(_journalFilePath))
            {
                return new List<ActivityRecord>();
            }

            var info = new FileInfo(_journalFilePath);
            if (info.Length > MaxJournalFileBytes)
            {
                persistenceStatusMessage = "The saved Activity journal exceeded its safety limit and was not loaded. New activity is still tracked in memory.";
                return new List<ActivityRecord>();
            }

            var records = JsonSerializer.Deserialize<List<ActivityRecord>>(File.ReadAllText(_journalFilePath), PersistenceOptions);
            if (records is null)
            {
                persistenceStatusMessage = "The saved Activity journal could not be read. New activity is still tracked in memory.";
                return new List<ActivityRecord>();
            }

            return records.Where(r => r is not null).OrderBy(r => r.StartedAt)
                .TakeLast(MaxPersistedRecords)
                .Select(r => r.State == ActivityState.Running ? r with
                {
                    State = ActivityState.NeedsAttention,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Result = "The previous session ended before this operation reported an outcome. Review the system state before retrying.",
                    UndoAvailable = false,
                } : r).ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            persistenceStatusMessage = "The saved Activity journal is unavailable. New activity is still tracked in memory, but history may not survive restart.";
            System.Diagnostics.Debug.WriteLine($"[ActivityJournal] Load failed: {ex}");
            return new List<ActivityRecord>();
        }
    }

    private ActivityRecord[] SnapshotForPersistence() =>
        _records.TakeLast(MaxPersistedRecords).ToArray();

    private void QueueSave(ActivityRecord[] snapshot)
    {
        lock (_persistenceLock)
        {
            _persistenceTail = _persistenceTail.ContinueWith(
                _ => SaveSnapshotAsync(snapshot),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
        }
    }

    private async Task SaveSnapshotAsync(ActivityRecord[] snapshot)
    {
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_journalFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(snapshot, PersistenceOptions);
            temporaryPath = _journalFilePath + ".tmp." + Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(temporaryPath, json).ConfigureAwait(false);
            File.Move(temporaryPath, _journalFilePath, overwrite: true);
            temporaryPath = null;
            SetPersistenceStatus(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            SetPersistenceStatus("Activity is being tracked in memory, but WinCare cannot save the journal to disk. History may not survive restart.");
            System.Diagnostics.Debug.WriteLine($"[ActivityJournal] Save failed: {ex}");
        }
        finally
        {
            if (!string.IsNullOrEmpty(temporaryPath))
            {
                try { File.Delete(temporaryPath); } catch { }
            }
        }
    }

    private void SetPersistenceStatus(string? message)
    {
        bool changed;
        lock (_persistenceLock)
        {
            changed = !string.Equals(_persistenceStatusMessage, message, StringComparison.Ordinal);
            _persistenceStatusMessage = message;
        }

        if (changed)
        {
            RaiseChanged();
        }
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ActivityJournal] Changed subscriber failed: {ex}");
        }
    }

    private void TrimCompletedRecords()
    {
        // Preserve active operations so their eventual completion can still be recorded.
        int excess = _records.Count - MaxPersistedRecords;
        if (excess <= 0) return;
        _records.RemoveAll(record => record.State != ActivityState.Running && excess-- > 0);
    }
}
