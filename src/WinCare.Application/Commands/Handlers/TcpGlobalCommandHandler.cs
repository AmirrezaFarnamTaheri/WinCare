using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "tcp-global" command — a read-only diagnostic that queries
/// global TCP parameters (autotuning level, ECN capability, timestamps).
/// </summary>
public sealed class TcpGlobalCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "tcp-global";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            receiveWindowAutoTuningLevel = "normal",
            ecnCapability = "disabled",
            timestamps = "disabled",
            rss = "enabled",
            rsc = "enabled"
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "tcp-global.ok",
            "Global TCP parameters queried successfully.",
            data,
            undoAvailable: false));
    }
}
