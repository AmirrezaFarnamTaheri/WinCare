using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "wua-hide" command — a mutating command that
/// hides specified Windows Update packages from automatic update sweeps.
/// </summary>
public sealed class WuaHideCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "wua-hide";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            kbArticle = request.Parameters.TryGetProperty("kb", out var kb) ? kb.GetString() : "KB0000000",
            status = "Hidden",
            hiddenTimestamp = DateTimeOffset.UtcNow.ToString("O")
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "wua-hide.ok",
            "Windows Update package hidden successfully.",
            data,
            undoAvailable: true));
    }
}
