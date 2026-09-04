using System.Text.Json;
using WinCare.Domain.Activity;
using WinCare.Domain.Serialization;

namespace WinCare.Application.Activity;

/// <summary>
/// Thread-safe, append-only journal for command activity records, persisted to disk so the
/// audit trail survives application restarts.
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
    private readonly object _lock = new();

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

        _records = LoadCore();
    }

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
            SaveCore();
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
            SaveCore();
        }
    }

    private List<ActivityRecord> LoadCore()
    {
        try
        {
            if (!File.Exists(_journalFilePath))
            {
                return new List<ActivityRecord>();
            }

            var info = new FileInfo(_journalFilePath);
            if (info.Length > MaxJournalFileBytes)
            {
                return new List<ActivityRecord>();
            }

            var records = JsonSerializer.Deserialize<List<ActivityRecord>>(File.ReadAllText(_journalFilePath), PersistenceOptions);
            if (records is null)
            {
                return new List<ActivityRecord>();
            }

            return records.OrderBy(r => r.StartedAt).TakeLast(MaxPersistedRecords).ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<ActivityRecord>();
        }
    }

    private void SaveCore()
    {
        try
        {
            var directory = Path.GetDirectoryName(_journalFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tail = _records.OrderBy(r => r.StartedAt).TakeLast(MaxPersistedRecords).ToList();
            var json = JsonSerializer.Serialize(tail, PersistenceOptions);
            var temporaryPath = _journalFilePath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _journalFilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The in-memory journal remains authoritative even if disk persistence fails.
        }
    }
}
