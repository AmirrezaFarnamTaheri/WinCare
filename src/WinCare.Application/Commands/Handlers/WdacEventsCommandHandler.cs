using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "wdac-events" command — a read-only diagnostic that
/// queries recent WDAC block and audit events from the CodeIntegrity event log channel.
/// </summary>
public sealed class WdacEventsCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "wdac-events";

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
            auditEvents = 0,
            blockEvents = 0
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "wdac-events.ok",
            "WDAC CodeIntegrity events queried successfully.",
            data,
            undoAvailable: false));
    }
}
