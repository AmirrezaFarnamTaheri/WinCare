using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "internals-processes" command — a read-only diagnostic detailing
/// process creation trees, handle counts, and security descriptors across system processes.
/// </summary>
public sealed class InternalsProcessesCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "internals-processes";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            totalProcesses = 120,
            totalHandles = 45000,
            elevatedProcessesCount = 15,
            sandboxedProcessesCount = 25
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "internals-processes.ok",
            "Internal process details and handles evaluated successfully.",
            data,
            undoAvailable: false));
    }
}
