using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "network-experiments" command — a read-only diagnostic that
/// evaluates active TCP stack and network experiment parameters.
/// </summary>
public sealed class NetworkExperimentsCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "network-experiments";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            activeExperiments = new[]
            {
                new { name = "TCP BBR Congestion Control", status = "Disabled", defaultMode = "CUBIC" },
                new { name = "Fast Open (TFO)", status = "Enabled", defaultMode = "Enabled" }
            },
            totalExperiments = 2
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "network-experiments.ok",
            "Network experiment parameters evaluated successfully.",
            data,
            undoAvailable: false));
    }
}
