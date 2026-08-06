namespace WinCare.Domain.Activity;

/// <summary>
/// States for a dispatched activity’s lifecycle.
/// </summary>
public enum ActivityState
{
    /// <summary>Activity is still running.</summary>
    Running,
    /// <summary>Activity requests user attention.</summary>
    NeedsAttention,
    /// <summary>Activity completed without error.</summary>
    Completed,
    /// <summary>Activity failed.</summary>
    Failed,
    /// <summary>Activity was cancelled.</summary>
    Cancelled,
}

/// <summary>
/// Durable record of a command’s dispatched lifecycle.
/// </summary>
/// <param name="Id">Stable activity ID.</param>
/// <param name="CommandId">Catalog command ID.</param>
/// <param name="Title">Display title.</param>
/// <param name="State">Lifecycle state.</param>
/// <param name="StartedAt">Start time.</param>
/// <param name="CompletedAt">Completion time, when the activity is no longer running.</param>
/// <param name="Result">Human-readable outcome.</param>
/// <param name="UndoAvailable">Whether an undo is available.</param>
public sealed record ActivityRecord(
    Guid Id,
    string CommandId,
    string Title,
    ActivityState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Result,
    bool UndoAvailable);
