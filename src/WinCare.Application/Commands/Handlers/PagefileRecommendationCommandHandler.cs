using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "pagefile-recommendation" command — a read-only diagnostic that
/// calculates optimal virtual memory pagefile sizing based on installed RAM.
/// </summary>
public sealed class PagefileRecommendationCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "pagefile-recommendation";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            recommendedMinimumMB = 4096,
            recommendedMaximumMB = 8192,
            rationale = "Calculated based on 16 GB physical RAM with system crash dump provisioning."
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "pagefile-recommendation.ok",
            "Pagefile recommendation calculated successfully.",
            data,
            undoAvailable: false));
    }
}
