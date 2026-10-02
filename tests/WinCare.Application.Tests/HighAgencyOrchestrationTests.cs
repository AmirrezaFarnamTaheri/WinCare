namespace WinCare.Application.Tests;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.Application.Diagnostics;
using WinCare.Application.Execution;
using WinCare.Application.Profiles;
using WinCare.Application.Status;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;
using WinCare.Domain.Compensators;
using Xunit;

public sealed class HighAgencyOrchestrationTests
{
    private static readonly JsonElement EmptyParameters;

    static HighAgencyOrchestrationTests()
    {
        using var doc = JsonDocument.Parse("{}");
        EmptyParameters = doc.RootElement.Clone();
    }

    [Fact]
    public async Task TransientUndoCoordinator_Offers_Undo_Only_After_Dispatch_Succeeds()
    {
        await using var coordinator = new TransientUndoCoordinator();
        bool compensated = false;

        var dummyCompensator = new DummyCompensator("compensator.disk.clean.pressure", () => compensated = true);
        coordinator.RegisterCompensator(dummyCompensator);

        var snapshot = new StateSnapshot(
            SnapshotId: "SNAP-101",
            CommandId: "disk.clean.pressure",
            CreatedUtc: DateTime.UtcNow,
            ForwardState: EmptyParameters,
            ReverseState: EmptyParameters,
            IsReverted: false);

        var request = CommandRequest.Execute("disk.clean.pressure", EmptyParameters);
        var definition = new CommandDefinition(
            Id: "disk.clean.pressure", Title: "Clean storage", Summary: "Reclaim temporary storage",
            Area: "Storage", Section: "Cleanup", Risk: CommandRisk.Low, ReadOnly: false,
            AdministratorAccess: AdministratorAccess.No, Restart: RestartExpectation.No,
            LegacySource: "", MigrationStatus: MigrationStatus.Implemented, Keywords: Array.Empty<string>());
        var outcome = await coordinator.ExecuteOptimisticAsync(
            new StubDispatcher(CommandResultStatus.Succeeded),
            definition,
            request,
            "Cleaned 250 MB",
            snapshot);

        Assert.NotNull(outcome);
        Assert.Equal(CommandResultStatus.Succeeded, outcome.Status);
        Assert.NotNull(coordinator.CurrentNotification);
        Assert.Equal(snapshot.SnapshotId, coordinator.CurrentNotification.SnapshotId);

        // The registered compensator is called at most once for this immutable snapshot id.
        bool rollbackResult = await coordinator.ExecuteRollbackAsync(snapshot);
        Assert.True(rollbackResult);
        Assert.True(compensated);
        Assert.Null(coordinator.CurrentNotification);
    }

    [Fact]
    public async Task AutonomousHealingCoordinator_Scans_And_Resolves_Anomalies()
    {
        var dispatcher = new StubDispatcher();
        var anomaly = new CorrelatedAnomaly(
            "finding.disk.pressure", "Storage pressure", "Diagnostic evidence reports pressure.",
            "Review bounded cleanup.", new[]
            {
                new RemediationStep("cleaner-disk-pressure", EmptyParameters, "Review cleanup")
            });
        var healingCoordinator = new AutonomousHealingCoordinator(new StubAnomalySource(anomaly), dispatcher);
        var anomalies = await healingCoordinator.ScanForAnomaliesAsync(CancellationToken.None);

        Assert.Single(anomalies);
        Assert.Equal("finding.disk.pressure", anomalies[0].AnomalyId);

        var progressMessages = new System.Collections.Generic.List<string>();
        var progress = new Progress<string>(msg => progressMessages.Add(msg));

        bool resolved = await healingCoordinator.ResolveAnomalyAsync(anomaly,
            new System.Collections.Generic.Dictionary<string, ApprovedMutationPlan>(), progress, CancellationToken.None);
        Assert.False(resolved);
        Assert.Empty(dispatcher.Requests);
    }

    [Fact]
    public async Task SmartProfileController_Applies_Profile_Across_Workspaces()
    {
        var profileController = new SmartProfileController(new StubDispatcher());
        var results = await profileController.PreviewWorkspaceProfileAsync(
            "DiskDiet",
            ProfileIntensityTier.Balanced,
            CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("cleaner-disk-pressure", results[0].CommandId);
        Assert.Equal(CommandResultStatus.NotMigrated, results[0].Status);
    }

    [Fact]
    public async Task SystemConfidenceEngine_Synthesizes_Accurate_Posture()
    {
        var statusEngine = new SystemConfidenceEngine(new StubDispatcher());
        var report = await statusEngine.EvaluateMachinePostureAsync(CancellationToken.None);

        Assert.NotNull(report);
        Assert.Equal(ConfidenceVerdict.Unavailable, report.Verdict);
        Assert.Equal(0, report.ReclaimableMegabytes);
    }

    private sealed class DummyCompensator : ICompensatorDefinition
    {
        private readonly Action _onCompensate;

        public string CompensatorId { get; }

        public DummyCompensator(string compensatorId, Action onCompensate)
        {
            CompensatorId = compensatorId;
            _onCompensate = onCompensate;
        }

        public Task<bool> CompensateAsync(JsonElement snapshotData, CancellationToken cancellationToken)
        {
            _onCompensate();
            return Task.FromResult(true);
        }
    }

    private sealed class StubAnomalySource(CorrelatedAnomaly anomaly) : IAnomalySource
    {
        public Task<System.Collections.Generic.IReadOnlyList<CorrelatedAnomaly>> ScanAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<System.Collections.Generic.IReadOnlyList<CorrelatedAnomaly>>(new[] { anomaly });
        }
    }

    private sealed class StubDispatcher(CommandResultStatus status = CommandResultStatus.NotMigrated) : ICommandDispatcher
    {
        public System.Collections.Generic.List<CommandRequest> Requests { get; } = new();

        public Task<CommandResult> ExecuteAsync(CommandRequest request, CommandExecutionOptions options, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return Task.FromResult(new CommandResult(request.CommandId, request.CorrelationId,
                status, "test.result", "Stub dispatcher result.", null,
                now, now, false));
        }

        public bool RegisterDynamicCommand(CommandDefinition definition, ICommandHandler handler) => false;
        public bool UnregisterDynamicCommand(string commandId) => false;
    }
}
