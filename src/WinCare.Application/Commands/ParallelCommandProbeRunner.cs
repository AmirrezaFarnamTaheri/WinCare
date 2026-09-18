using WinCare.Domain.Commands;

namespace WinCare.Application.Commands;

/// <summary>
/// Runs read-only diagnostic probes concurrently with bounded concurrency to minimize checkup latency.
/// Preserves exact request order in the returned results.
/// </summary>
public static class ParallelCommandProbeRunner
{
    private const int DefaultMaxConcurrency = 4;

    /// <summary>
    /// Runs read-only preview probes in parallel, mapping outputs in request order.
    /// </summary>
    public static async Task<IReadOnlyList<CommandResult>> RunPreviewsAsync(
        ICommandDispatcher dispatcher,
        IReadOnlyList<string> commandIds,
        TimeSpan perProbeBudget,
        int maxConcurrency = DefaultMaxConcurrency,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(commandIds);
        if (perProbeBudget <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(perProbeBudget), "Per-probe budget must be positive.");
        }
        if (maxConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Concurrency must be positive.");
        }

        foreach (string commandId in commandIds)
        {
            if (string.IsNullOrWhiteSpace(commandId))
            {
                throw new ArgumentException("Probe command IDs must be non-empty.", nameof(commandIds));
            }
        }

        if (commandIds.Count == 0)
        {
            return Array.Empty<CommandResult>();
        }

        using SemaphoreSlim semaphore = new(maxConcurrency, maxConcurrency);
        Task<CommandResult>[] tasks = new Task<CommandResult>[commandIds.Count];

        for (int i = 0; i < commandIds.Count; i++)
        {
            string commandId = commandIds[i];
            tasks[i] = RunSingleProbeAsync(dispatcher, commandId, perProbeBudget, semaphore, cancellationToken);
        }

        CommandResult[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return Array.AsReadOnly(results);
    }

    private static async Task<CommandResult> RunSingleProbeAsync(
        ICommandDispatcher dispatcher,
        string commandId,
        TimeSpan perProbeBudget,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        CommandRequest request = CommandRequest.Preview(commandId);
        try
        {
            return await dispatcher.ExecuteAsync(
                request,
                new CommandExecutionOptions(
                    ReviewApproved: false,
                    Deadline: startedAt + perProbeBudget),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A diagnostics product cannot afford to discard the reason a measurement failed, and
            // the synthesized result must stay correlated with the probe request that produced it.
            System.Diagnostics.Debug.WriteLine($"[ParallelCommandProbeRunner] Probe '{commandId}' faulted: {ex.GetType().Name} - {ex.Message}");
            DateTimeOffset completedAt = DateTimeOffset.UtcNow;
            return new CommandResult(
                commandId,
                request.CorrelationId,
                CommandResultStatus.Failed,
                "probe.dispatch_exception",
                $"WinCare could not complete this read-only measurement probe: {ex.GetType().Name} - {ex.Message}",
                null,
                startedAt,
                completedAt,
                false);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
