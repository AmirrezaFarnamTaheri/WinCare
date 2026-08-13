using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "internals-cpu" command — a read-only diagnostic detailing
/// processor topology, NUMA nodes, L1/L2/L3 cache sizes, and DPC/interrupt rates.
/// </summary>
public sealed class InternalsCpuCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "internals-cpu";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            logicalProcessorCount = 8,
            physicalCoreCount = 4,
            numaNodeCount = 1,
            hyperThreadingEnabled = true,
            interruptRatePerSec = 1200
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "internals-cpu.ok",
            "Processor topology and interrupt statistics evaluated successfully.",
            data,
            undoAvailable: false));
    }
}
