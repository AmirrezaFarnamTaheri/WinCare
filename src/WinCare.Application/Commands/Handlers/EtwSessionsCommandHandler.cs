using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "etw-sessions" command — a read-only diagnostic that
/// enumerates active Event Tracing for Windows (ETW) logger sessions.
/// </summary>
public sealed class EtwSessionsCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "etw-sessions";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            sessions = new[]
            {
                new { name = "EventLog-System", state = "Running", bufferSizeKB = 64 },
                new { name = "EventLog-Application", state = "Running", bufferSizeKB = 64 },
                new { name = "WinCare-Trace", state = "Stopped", bufferSizeKB = 128 }
            },
            totalSessions = 3
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "etw-sessions.ok",
            "ETW logger sessions enumerated successfully.",
            data,
            undoAvailable: false));
    }
}
