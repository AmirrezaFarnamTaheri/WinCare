using WinCare.Domain.Commands;

namespace WinCare.Application.Commands;

/// <summary>
/// Runs read-only measurement probes one at a time so the load created by one probe cannot
/// perturb evidence gathered by another probe. Results preserve request order.
/// </summary>
public static class SequentialCommandProbeRunner
{
    // Source-Driven Development Citation:
    // Pattern: Dependency Inversion - Program to an interface, not an implementation
    // Source: https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/types/interfaces
    // "Interfaces define contracts that decouple callers from specific concrete implementations."
    public static async Task<IReadOnlyList<CommandResult>> RunPreviewsAsync(
        ICommandDispatcher dispatcher,
        IReadOnlyList<string> commandIds,
        TimeSpan perProbeBudget,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(commandIds);
        if (perProbeBudget <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(perProbeBudget), "Per-probe budget must be positive.");
        }

        var results = new List<CommandResult>(commandIds.Count);
        foreach (string commandId in commandIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(commandId))
            {
                throw new ArgumentException("Probe command IDs must be non-empty.", nameof(commandIds));
            }

            DateTimeOffset startedAt = DateTimeOffset.UtcNow;
            CommandRequest request = CommandRequest.Preview(commandId);
            try
            {
                CommandResult result = await dispatcher.ExecuteAsync(
                    request,
                    new CommandExecutionOptions(
                        ReviewApproved: false,
                        Deadline: startedAt + perProbeBudget),
                    cancellationToken).ConfigureAwait(false);
                results.Add(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A diagnostics product cannot afford to discard the reason a measurement failed, and
                // the synthesized result must stay correlated with the probe request that produced it.
                System.Diagnostics.Debug.WriteLine($"[SequentialCommandProbeRunner] Probe '{commandId}' faulted: {ex.GetType().Name} - {ex.Message}");
                DateTimeOffset completedAt = DateTimeOffset.UtcNow;
                results.Add(new CommandResult(
                    commandId,
                    request.CorrelationId,
                    CommandResultStatus.Failed,
                    "probe.dispatch_exception",
                    $"WinCare could not complete this read-only measurement probe: {ex.GetType().Name} - {ex.Message}",
                    null,
                    startedAt,
                    completedAt,
                    false));
            }
        }

        return results;
    }
}
