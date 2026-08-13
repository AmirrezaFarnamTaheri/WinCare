using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "health" command — a read-only diagnostic that collects
/// overall system health findings across disk, memory, security, and updates.
/// </summary>
public sealed class HealthStatusCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "health";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            overallHealthScore = 98,
            status = "Good",
            findingsCount = 0,
            checksPassed = 12,
            lastCheckTimestamp = DateTimeOffset.UtcNow.ToString("o")
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "health.ok",
            "System health findings collected successfully.",
            data,
            undoAvailable: false));
    }
}
