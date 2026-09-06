using System.Diagnostics;
using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;
using Xunit;

namespace WinCare.Application.Tests;

public sealed class ParallelCommandProbeRunnerTests
{
    private static CommandDefinition CreateDef(string id) =>
        new(
            id,
            id,
            $"Test {id}",
            "Checkup",
            "Probes",
            CommandRisk.ReadOnly,
            ReadOnly: true,
            AdministratorAccess.No,
            RestartExpectation.No,
            "test",
            MigrationStatus.Implemented,
            [id],
            RiskTier.Safe);

    private sealed class ConcurrencyTracker
    {
        private readonly object _gate = new();
        private int _active;

        public int Peak { get; private set; }

        public void Enter()
        {
            lock (_gate)
            {
                _active++;
                Peak = Math.Max(Peak, _active);
            }
        }

        public void Exit()
        {
            lock (_gate)
            {
                _active--;
            }
        }
    }

    private sealed class DelayedProbeHandler(string commandId, TimeSpan delay, ConcurrencyTracker? tracker = null) : ICommandHandler
    {
        public string CommandId { get; } = commandId;

        public async Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            tracker?.Enter();
            try
            {
                await Task.Delay(delay, cancellationToken);
                return CommandHandlerOutcome.Succeeded(
                    $"{CommandId}.ok",
                    $"{CommandId} completed",
                    JsonSerializer.SerializeToElement(new { id = CommandId }));
            }
            finally
            {
                tracker?.Exit();
            }
        }
    }

    [Fact]
    public async Task Parallel_runner_executes_probes_concurrently_and_preserves_order()
    {
        string[] commandIds = ["probe-1", "probe-2", "probe-3"];
        CommandDefinition[] defs = commandIds.Select(CreateDef).ToArray();
        var tracker = new ConcurrencyTracker();
        ICommandHandler[] handlers =
        [
            new DelayedProbeHandler("probe-1", TimeSpan.FromMilliseconds(150), tracker),
            new DelayedProbeHandler("probe-2", TimeSpan.FromMilliseconds(150), tracker),
            new DelayedProbeHandler("probe-3", TimeSpan.FromMilliseconds(150), tracker)
        ];

        CommandDispatcher dispatcher = new(defs, handlers);

        Stopwatch sw = Stopwatch.StartNew();
        IReadOnlyList<CommandResult> results = await ParallelCommandProbeRunner.RunPreviewsAsync(
            dispatcher,
            commandIds,
            TimeSpan.FromSeconds(5),
            maxConcurrency: 3,
            CancellationToken.None);
        sw.Stop();

        Assert.Equal(3, results.Count);
        Assert.Equal("probe-1", results[0].CommandId);
        Assert.Equal("probe-2", results[1].CommandId);
        Assert.Equal("probe-3", results[2].CommandId);
        Assert.All(results, r => Assert.Equal(CommandResultStatus.Succeeded, r.Status));

        Assert.True(tracker.Peak > 1, $"Expected overlapping probe execution, observed peak concurrency {tracker.Peak}.");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"Parallel probe test exceeded loose 3s bound: {sw.Elapsed}.");
    }

    [Fact]
    public async Task Parallel_runner_isolates_individual_probe_failures()
    {
        string[] commandIds = ["good-1", "faulty", "good-2"];
        CommandDefinition[] defs = commandIds.Select(CreateDef).ToArray();

        var handlers = new ICommandHandler[]
        {
            new DelayedProbeHandler("good-1", TimeSpan.FromMilliseconds(10)),
            new FaultyHandler("faulty"),
            new DelayedProbeHandler("good-2", TimeSpan.FromMilliseconds(10))
        };

        CommandDispatcher dispatcher = new(defs, handlers);

        IReadOnlyList<CommandResult> results = await ParallelCommandProbeRunner.RunPreviewsAsync(
            dispatcher,
            commandIds,
            TimeSpan.FromSeconds(5),
            maxConcurrency: 3,
            CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.Equal(CommandResultStatus.Succeeded, results[0].Status);
        Assert.Equal(CommandResultStatus.Failed, results[1].Status);
        Assert.Equal(CommandResultStatus.Succeeded, results[2].Status);
    }

    private sealed class FaultyHandler(string commandId) : ICommandHandler
    {
        public string CommandId { get; } = commandId;

        public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Simulated probe explosion");
        }
    }
}
