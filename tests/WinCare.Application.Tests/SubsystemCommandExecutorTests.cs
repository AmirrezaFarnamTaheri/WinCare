namespace WinCare.Application.Tests;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.Application.Commands.Subsystems;
using WinCare.Domain.Commands;
using Xunit;

public sealed class SubsystemCommandExecutorTests
{
    private static readonly JsonElement EmptyParameters;

    static SubsystemCommandExecutorTests()
    {
        using var doc = JsonDocument.Parse("{}");
        EmptyParameters = doc.RootElement.Clone();
    }

    [Fact]
    public void StorageReclamationHandler_CanHandle_Matches_Storage_Prefixes()
    {
        var handler = new StorageReclamationHandler();
        Assert.Equal("Storage", handler.Subsystem);
        Assert.True(handler.CanHandle("disk.clean.pressure"));
        Assert.True(handler.CanHandle("cleaner.temporary.files"));
        Assert.True(handler.CanHandle("cache.delivery.optimization"));
        Assert.False(handler.CanHandle("driver.inf.audit"));
        Assert.False(handler.CanHandle(null!));
    }

    [Fact]
    public void DismServicingHandler_CanHandle_Matches_Servicing_Prefixes()
    {
        var handler = new DismServicingHandler();
        Assert.Equal("Servicing", handler.Subsystem);
        Assert.True(handler.CanHandle("dism.cleanup.components"));
        Assert.True(handler.CanHandle("appx.package.inventory"));
        Assert.False(handler.CanHandle("disk.clean.pressure"));
        Assert.False(handler.CanHandle(""));
    }

    [Fact]
    public void DriverSecurityHandler_CanHandle_Matches_Security_Prefixes()
    {
        var handler = new DriverSecurityHandler();
        Assert.Equal("Security", handler.Subsystem);
        Assert.True(handler.CanHandle("driver.store.audit"));
        Assert.True(handler.CanHandle("hvci.memory.integrity"));
        Assert.False(handler.CanHandle("cleaner.temp"));
    }

    [Fact]
    public void RemediationPolicyHandler_CanHandle_Matches_Remediation_Prefixes()
    {
        var handler = new RemediationPolicyHandler();
        Assert.Equal("Remediation", handler.Subsystem);
        Assert.True(handler.CanHandle("remediation.baseline.apply"));
        Assert.True(handler.CanHandle("policy.telemetry.harden"));
        Assert.False(handler.CanHandle("dism.check"));
    }

    [Fact]
    public void SubsystemCommandRegistry_Registers_Resolves_And_Queries_Handlers()
    {
        var registry = new SubsystemCommandRegistry();
        var storage = new StorageReclamationHandler();
        var servicing = new DismServicingHandler();
        var security = new DriverSecurityHandler();
        var remediation = new RemediationPolicyHandler();

        registry.Register(storage);
        registry.Register(servicing);
        registry.Register(security);
        registry.Register(remediation);

        // Deduplication
        registry.Register(storage);
        Assert.Equal(4, registry.Executors.Count);

        Assert.True(registry.CanHandle("disk.clean.pressure"));
        Assert.True(registry.CanHandle("dism.cleanup.components"));
        Assert.True(registry.CanHandle("driver.store.audit"));
        Assert.True(registry.CanHandle("remediation.baseline.apply"));
        Assert.False(registry.CanHandle("nonexistent.tool"));

        Assert.Same(storage, registry.Resolve("disk.clean.pressure"));
        Assert.Same(servicing, registry.Resolve("dism.cleanup.components"));
        Assert.Same(security, registry.Resolve("driver.store.audit"));
        Assert.Same(remediation, registry.Resolve("remediation.baseline.apply"));

        var storageHandlers = registry.GetBySubsystem("Storage");
        Assert.Single(storageHandlers);
        Assert.Same(storage, storageHandlers[0]);

        // Unregister & Clear
        Assert.True(registry.Unregister(storage));
        Assert.Equal(3, registry.Executors.Count);
        Assert.Null(registry.Resolve("disk.clean.pressure"));

        registry.Clear();
        Assert.Empty(registry.Executors);
    }

    [Fact]
    public async Task SubsystemHandlers_ExecuteAsync_Produce_Successful_Outcome()
    {
        var handler = new StorageReclamationHandler();
        var request = CommandRequest.Execute("disk.clean.pressure", EmptyParameters);
        var outcome = await handler.ExecuteAsync(null!, request, CancellationToken.None);

        Assert.NotNull(outcome);
        Assert.True(outcome.Success);
    }

    [Fact]
    public async Task SubsystemHandlers_ExecuteAsync_Respects_CancellationToken()
    {
        var handler = new StorageReclamationHandler();
        var request = CommandRequest.Execute("disk.clean.pressure", EmptyParameters);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handler.ExecuteAsync(null!, request, cts.Token));
    }

    [Fact]
    public void SubsystemHandlers_PlanPreview_Returns_Valid_Preview()
    {
        var handler = new StorageReclamationHandler();
        var preview = handler.PlanPreview(null!, CommandParameters.Empty);

        Assert.NotNull(preview);
        Assert.Equal("Storage", preview.Subsystem);
        Assert.NotEmpty(preview.AffectedTargets);
        Assert.True(preview.EstimatedImpactBytes > 0);
        Assert.False(preview.RequiresElevation);

        var securityHandler = new DriverSecurityHandler();
        var secPreview = securityHandler.PlanPreview(null!, CommandParameters.Empty);
        Assert.True(secPreview.RequiresElevation);
    }

    [Fact]
    public void CommandParameters_Parses_Json_And_Extracts_Types()
    {
        var json = "{\"path\":\"C:\\\\Temp\",\"count\":42,\"dryRun\":true}";
        var parameters = CommandParameters.FromJson(json);

        Assert.True(parameters.ContainsKey("path"));
        Assert.True(parameters.ContainsKey("count"));
        Assert.True(parameters.ContainsKey("dryRun"));
        Assert.False(parameters.ContainsKey("missing"));

        Assert.True(parameters.TryGetString("path", out var path));
        Assert.Equal("C:\\Temp", path);

        Assert.True(parameters.TryGetInt64("count", out var count));
        Assert.Equal(42L, count);

        Assert.True(parameters.TryGetBoolean("dryRun", out var dryRun));
        Assert.True(dryRun);

        // Invalid JSON fallback
        var fallback = CommandParameters.FromJson("not a json");
        Assert.NotNull(fallback);
        Assert.False(fallback.ContainsKey("anything"));
    }
}
