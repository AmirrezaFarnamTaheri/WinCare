using System.Text.Json;

namespace WinCare.Domain.Commands;

/// <summary>
/// Enumerates the terminal status of a dispatched command.
/// </summary>
public enum CommandResultStatus
{
    /// <summary>Execution succeeded.</summary>
    Succeeded,
    /// <summary>Admission or policy blocked execution.</summary>
    Blocked,
    /// <summary>Execution was cancelled.</summary>
    Cancelled,
    /// <summary>Execution failed.</summary>
    Failed,
    /// <summary>The command catalog is present but the native implementation is not ready.</summary>
    NotMigrated,
}

/// <summary>
/// Typed dispatch result, including timing and recovery metadata.
/// </summary>
/// <param name="CommandId">Stable catalog command ID.</param>
/// <param name="CorrelationId">Correlation ID copied from the request.</param>
/// <param name="Status">Terminal status.</param>
/// <param name="Code">Stable result code.</param>
/// <param name="Message">Human-readable outcome.</param>
/// <param name="Data">Optional structured payload.</param>
/// <param name="StartedAt">Dispatch start time.</param>
/// <param name="CompletedAt">Dispatch completion time.</param>
/// <param name="UndoAvailable">Whether an undo / compensator is available.</param>
public sealed record CommandResult(
    string CommandId,
    Guid CorrelationId,
    CommandResultStatus Status,
    string Code,
    string Message,
    JsonElement? Data,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool UndoAvailable)
{
    /// <summary>
    /// Gets the wall-clock duration of the dispatch.
    /// </summary>
    public TimeSpan Duration => CompletedAt - StartedAt;
}
