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
    public async Task SubsystemHandlers_Fail_Closed_Without_An_Implementation()
    {
        var handler = new StorageReclamationHandler();
        var definition = WinCare.CommandCatalog.CommandCatalog.Find("cleaner-disk-pressure")!;
        var request = CommandRequest.Execute(definition.Id, EmptyParameters);
        var outcome = await handler.ExecuteAsync(definition, request, CancellationToken.None);

        Assert.NotNull(outcome);
        Assert.Equal(CommandResultStatus.NotMigrated, outcome.Status);
        Assert.Contains("No files were changed", outcome.Message);
    }

    [Fact]
    public async Task SubsystemHandlers_ExecuteAsync_Respects_CancellationToken()
    {
        var handler = new StorageReclamationHandler();
        var definition = WinCare.CommandCatalog.CommandCatalog.Find("cleaner-disk-pressure")!;
        var request = CommandRequest.Execute(definition.Id, EmptyParameters);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handler.ExecuteAsync(definition, request, cts.Token));
    }

    [Fact]
    public void SubsystemHandlers_PlanPreview_Returns_Valid_Preview()
    {
        var handler = new StorageReclamationHandler();
        var definition = WinCare.CommandCatalog.CommandCatalog.Find("cleaner-disk-pressure")!;
        var preview = handler.PlanPreview(definition, SubsystemCommandParameters.Empty);

        Assert.NotNull(preview);
        Assert.Equal("Storage", preview.Subsystem);
        Assert.Empty(preview.AffectedTargets);
        Assert.Equal(0, preview.EstimatedImpactBytes);
        Assert.True(preview.RequiresElevation);

        var securityHandler = new DriverSecurityHandler();
        var secPreview = securityHandler.PlanPreview(definition, SubsystemCommandParameters.Empty);
        Assert.True(secPreview.RequiresElevation);
    }

    [Fact]
    public void SubsystemCommandParameters_Parses_Json_And_Extracts_Types()
    {
        var json = "{\"path\":\"C:\\\\Temp\",\"count\":42,\"dryRun\":true}";
        var parameters = SubsystemCommandParameters.FromJson(json);

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

        // Malformed input must not silently turn into an empty, potentially executable request.
        Assert.Throws<JsonException>(() => SubsystemCommandParameters.FromJson("not a json"));
    }
}
