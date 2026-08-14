using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands;

/// <summary>
/// Handler-level execution result before it is promoted to a domain <see cref="CommandResult"/>.
/// </summary>
/// <param name="Status">Result status produced by the handler.</param>
/// <param name="Code">Stable, dot-separated result code.</param>
/// <param name="Message">Human-readable outcome.</param>
/// <param name="Data">Optional structured payload.</param>
/// <param name="UndoAvailable">Whether the operation can be undone through recovery.</param>
public sealed record CommandHandlerOutcome(
    CommandResultStatus Status,
    string Code,
    string Message,
    JsonElement? Data,
    bool UndoAvailable)
{
    /// <summary>
    /// Creates a successful outcome.
    /// </summary>
    public static CommandHandlerOutcome Succeeded(
        string code,
        string message,
        JsonElement? data = null,
        bool undoAvailable = false) =>
        new(CommandResultStatus.Succeeded, code, message, data, undoAvailable);

    /// <summary>
    /// Creates a blocked outcome.
    /// </summary>
    public static CommandHandlerOutcome Blocked(string code, string message) =>
        new(CommandResultStatus.Blocked, code, message, null, UndoAvailable: false);

    /// <summary>
    /// Creates a failed outcome for an unrecoverable handler error.
    /// </summary>
    public static CommandHandlerOutcome Failed(
        string code,
        string message,
        JsonElement? data = null) =>
        new(CommandResultStatus.Failed, code, message, data, UndoAvailable: false);
}
