using WinCare.Domain.Activity;

namespace WinCare.Application.Activity;

/// <summary>
/// Interface for thread-safe in-process activity logging of command records.
/// </summary>
public interface IActivityJournalService
{
    /// <summary>Raised when in-memory records or durable-persistence health changes.</summary>
    event EventHandler? Changed;

    /// <summary>Returns a snapshot of all records.</summary>
    IReadOnlyList<ActivityRecord> GetAll();

    /// <summary>Whether the latest queued activity snapshot was durably persisted.</summary>
    bool IsPersistenceHealthy { get; }

    /// <summary>User-safe persistence status when durability is degraded.</summary>
    string? PersistenceStatusMessage { get; }

    /// <summary>Begins a new activity record in the Running state.</summary>
    ActivityRecord Begin(string commandId, string title);

    /// <summary>Transitions the record to Completed.</summary>
    void Complete(Guid id, string result, bool undoAvailable = false);

    /// <summary>Transitions the record to Failed.</summary>
    void Fail(Guid id, string result);

    /// <summary>Transitions the record to Cancelled.</summary>
    void Cancel(Guid id);

    /// <summary>Transitions the record to NeedsAttention with a diagnostic message.</summary>
    void RequireAttention(Guid id, string message);

    /// <summary>Waits for all persistence work queued before the call to finish.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
