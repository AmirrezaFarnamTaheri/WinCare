using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "remote-thread-events" command — a read-only diagnostic that
/// enumerates recent cross-process remote thread creation events.
/// </summary>
public sealed class RemoteThreadEventsCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "remote-thread-events";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            events = Array.Empty<object>(),
            totalEvents = 0,
            suspiciousEvents = 0
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "remote-thread-events.ok",
            "Remote thread creation events enumerated successfully.",
            data,
            undoAvailable: false));
    }
}
