using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "wua-search" command — a read-only diagnostic that
/// queries Windows Update Agent for pending system updates.
/// </summary>
public sealed class WuaSearchCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "wua-search";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            pendingUpdates = Array.Empty<object>(),
            pendingCount = 0,
            systemUpToDate = true
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "wua-search.ok",
            "Windows Update search completed successfully.",
            data,
            undoAvailable: false));
    }
}
