using System.Text.Json;
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

        Task<CommandResult> first = dispatcher.ExecuteAsync(Request("system"), CommandExecutionOptions.Default, CancellationToken.None);
        Task<CommandResult> second = dispatcher.ExecuteAsync(Request("storage"), CommandExecutionOptions.Default, CancellationToken.None);
        await Task.WhenAll(first, second);

        Assert.Equal(1, tracker.MaxConcurrent);
        Assert.Equal(CommandResultStatus.Succeeded, first.Result.Status);
        Assert.Equal(CommandResultStatus.Succeeded, second.Result.Status);
    }

    private static CommandDefinition Definition(string id) => new(
        id, id, "Measurement probe", "Checkup", "Evidence", CommandRisk.ReadOnly,
        ReadOnly: true, AdministratorAccess.No, RestartExpectation.No, "test",
        MigrationStatus.Implemented, [id]);

    private static CommandRequest Request(string id) =>
        CommandRequest.Preview(id, JsonSerializer.SerializeToElement(new { }));

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
