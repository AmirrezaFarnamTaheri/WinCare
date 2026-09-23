namespace WinCare.Application.Tests;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.Application.Commands.Subsystems;
using WinCare.Application.Diagnostics;
using WinCare.Application.Execution;
using WinCare.Application.Profiles;
using WinCare.Application.Status;
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
    public async Task TransientUndoCoordinator_Executes_And_RollsBack_Snapshot()
    {
        await using var coordinator = new TransientUndoCoordinator();
        var storageHandler = new StorageReclamationHandler();
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
        var outcome = await coordinator.ExecuteOptimisticAsync(
            storageHandler,
            null!,
            request,
            "Cleaned 250 MB",
            snapshot);

        Assert.NotNull(outcome);
        Assert.True(outcome.Success);
        Assert.NotNull(coordinator.CurrentNotification);
        Assert.Equal("SNAP-101", coordinator.CurrentNotification.SnapshotId);

        // Perform rollback
        bool rollbackResult = await coordinator.ExecuteRollbackAsync(snapshot);
        Assert.True(rollbackResult);
        Assert.True(compensated);
        Assert.Null(coordinator.CurrentNotification);
    }

    [Fact]
    public async Task AutonomousHealingCoordinator_Scans_And_Resolves_Anomalies()
    {
        var registry = new SubsystemCommandRegistry();
        registry.Register(new StorageReclamationHandler());
        registry.Register(new RemediationPolicyHandler());

        var healingCoordinator = new AutonomousHealingCoordinator(registry);
        var anomalies = await healingCoordinator.ScanForAnomaliesAsync(CancellationToken.None);

        Assert.NotEmpty(anomalies);
        var anomaly = anomalies[0];
        Assert.Equal("anomaly.disk.pressure.telemetry_loop", anomaly.AnomalyId);
        Assert.NotEmpty(anomaly.RemediationPipeline);

        var progressMessages = new System.Collections.Generic.List<string>();
        var progress = new Progress<string>(msg => progressMessages.Add(msg));

        bool resolved = await healingCoordinator.ResolveAnomalyInstantlyAsync(anomaly, progress, CancellationToken.None);
        Assert.True(resolved);
    }

    [Fact]
    public async Task SmartProfileController_Applies_Profile_Across_Workspaces()
    {
        var registry = new SubsystemCommandRegistry();
        registry.Register(new StorageReclamationHandler());
        registry.Register(new DismServicingHandler());

        var profileController = new SmartProfileController(registry);

        int applied = await profileController.ApplyWorkspaceProfileAsync(
            "DiskDiet",
            ProfileIntensityTier.Balanced,
            CancellationToken.None);

        Assert.True(applied > 0);
    }

    [Fact]
    public async Task SystemConfidenceEngine_Synthesizes_Accurate_Posture()
    {
        var registry = new SubsystemCommandRegistry();
        registry.Register(new StorageReclamationHandler());

        var statusEngine = new SystemConfidenceEngine(registry);
        var report = await statusEngine.EvaluateMachinePostureAsync(CancellationToken.None);

        Assert.NotNull(report);
        Assert.Equal(ConfidenceVerdict.ActionRecommended, report.Verdict);
        Assert.True(report.ReclaimableMegabytes > 0);
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
}
