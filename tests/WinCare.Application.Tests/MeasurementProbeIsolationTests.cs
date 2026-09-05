using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;
using Xunit;

namespace WinCare.Application.Tests;

public sealed class MeasurementProbeIsolationTests
{
    [Fact]
    public async Task Quick_check_measurement_probes_are_serialized()
    {
        var tracker = new ConcurrencyTracker();
        var definitions = new[] { Definition("system"), Definition("storage") };
        var dispatcher = new CommandDispatcher(definitions, new ICommandHandler[]
        {
            new DelayedHandler("system", tracker),
            new DelayedHandler("storage", tracker),
        });

        IReadOnlyList<CommandResult> results = await SequentialCommandProbeRunner.RunPreviewsAsync(
            dispatcher,
            ["system", "storage"],
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(1, tracker.MaxConcurrent);
        Assert.Equal(["system", "storage"], results.Select(result => result.CommandId));
        Assert.All(results, result => Assert.Equal(CommandResultStatus.Succeeded, result.Status));
    }

    [Fact]
    public async Task Sequential_probe_runner_is_not_a_global_dispatcher_lock()
    {
        var tracker = new ConcurrencyTracker();
        var definitions = new[] { Definition("system"), Definition("storage") };
        var dispatcher = new CommandDispatcher(definitions, new ICommandHandler[]
        {
            new DelayedHandler("system", tracker),
            new DelayedHandler("storage", tracker),
        });

        Task<CommandResult> first = dispatcher.ExecuteAsync(CommandRequest.Preview("system"), CommandExecutionOptions.Default, CancellationToken.None);
        Task<CommandResult> second = dispatcher.ExecuteAsync(CommandRequest.Preview("storage"), CommandExecutionOptions.Default, CancellationToken.None);
        await Task.WhenAll(first, second);

        Assert.True(tracker.MaxConcurrent > 1, "Independent dispatcher calls should remain concurrent; only the measurement runner serializes probes.");
    }

    private static CommandDefinition Definition(string id) => new(
        id, id, "Measurement probe", "Checkup", "Evidence", CommandRisk.ReadOnly,
        ReadOnly: true, AdministratorAccess.No, RestartExpectation.No, "test",
        MigrationStatus.Implemented, [id]);

    private sealed class ConcurrencyTracker
    {
        private int _current;
        private int _max;
        public int MaxConcurrent => Volatile.Read(ref _max);

        public void Enter()
        {
            int current = Interlocked.Increment(ref _current);
            int observed;
            while (current > (observed = Volatile.Read(ref _max)))
            {
                if (Interlocked.CompareExchange(ref _max, current, observed) == observed) break;
            }
        }

        public void Exit() => Interlocked.Decrement(ref _current);
    }

    private sealed class DelayedHandler(string commandId, ConcurrencyTracker tracker) : ICommandHandler
    {
        public string CommandId { get; } = commandId;

        public async Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            tracker.Enter();
            try
            {
                await Task.Delay(75, cancellationToken);
                return CommandHandlerOutcome.Succeeded("probe.ok", "Probe complete.");
            }
            finally
            {
                tracker.Exit();
            }
        }
    }
}
