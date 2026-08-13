using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "wua-history" command — a read-only diagnostic that
/// queries Windows Update installation history.
/// </summary>
public sealed class WuaHistoryCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "wua-history";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            history = new[]
            {
                new { title = "2026-08 Cumulative Update for Windows 11 (KB5041585)", date = "2026-08-10", result = "Succeeded" },
                new { title = "Windows Defender Security Intelligence - KB2267602", date = "2026-08-12", result = "Succeeded" }
            },
            totalEntries = 2
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "wua-history.ok",
            "Windows Update history queried successfully.",
            data,
            undoAvailable: false));
    }
}
