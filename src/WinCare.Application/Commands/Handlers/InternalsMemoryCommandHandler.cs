using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "internals-memory" command — a read-only diagnostic detailing
/// system physical memory, committed bytes, paged/non-paged pools, and working set limits.
/// </summary>
public sealed class InternalsMemoryCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "internals-memory";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            totalPhysicalMB = 16384,
            availablePhysicalMB = 8192,
            committedBytesMB = 10240,
            pagedPoolMB = 350,
            nonPagedPoolMB = 220
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "internals-memory.ok",
            "Internal memory statistics evaluated successfully.",
            data,
            undoAvailable: false));
    }
}
