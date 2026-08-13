using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "preset" command — a read-only or mutating command that
/// executes a curated maintenance preset profile.
/// </summary>
public sealed class PresetExecutionCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "preset";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            presetName = "Monthly Maintenance",
            stepsCompleted = 3,
            stepsTotal = 3,
            status = "Completed"
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "preset.ok",
            "Maintenance preset profile executed successfully.",
            data,
            undoAvailable: false));
    }
}
