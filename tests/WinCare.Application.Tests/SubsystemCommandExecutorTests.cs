namespace WinCare.Application.Tests;

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
    }

    [Fact]
    public void DismServicingHandler_CanHandle_Matches_Servicing_Prefixes()
    {
        var handler = new DismServicingHandler();
        Assert.Equal("Servicing", handler.Subsystem);
        Assert.True(handler.CanHandle("dism.cleanup.components"));
        Assert.True(handler.CanHandle("appx.package.inventory"));
        Assert.False(handler.CanHandle("disk.clean.pressure"));
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
    public void SubsystemCommandRegistry_Registers_And_Resolves_Handlers()
    {
        var registry = new SubsystemCommandRegistry();
        registry.Register(new StorageReclamationHandler());
        registry.Register(new DismServicingHandler());
        registry.Register(new DriverSecurityHandler());
        registry.Register(new RemediationPolicyHandler());

        Assert.Equal(4, registry.Executors.Count);

        var storage = registry.Resolve("disk.clean.pressure");
        Assert.NotNull(storage);
        Assert.Equal("Storage", storage.Subsystem);

        var servicing = registry.Resolve("dism.cleanup.components");
        Assert.NotNull(servicing);
        Assert.Equal("Servicing", servicing.Subsystem);

        var security = registry.Resolve("driver.store.audit");
        Assert.NotNull(security);
        Assert.Equal("Security", security.Subsystem);

        var remediation = registry.Resolve("remediation.baseline.apply");
        Assert.NotNull(remediation);
        Assert.Equal("Remediation", remediation.Subsystem);

        Assert.Null(registry.Resolve("nonexistent.tool"));
    }

    [Fact]
    public async Task SubsystemHandlers_ExecuteAsync_Produce_Successful_Outcome()
    {
        var handler = new StorageReclamationHandler();
        var request = CommandRequest.Execute("disk.clean.pressure", EmptyParameters);
        var outcome = await handler.ExecuteAsync(null, request, CancellationToken.None);

        Assert.NotNull(outcome);
        Assert.True(outcome.Success);
    }

    [Fact]
    public void SubsystemHandlers_PlanPreview_Returns_Valid_Preview()
    {
        var handler = new StorageReclamationHandler();
        var preview = handler.PlanPreview(null, CommandParameters.Empty);

        Assert.NotNull(preview);
        Assert.Equal("Storage", preview.Subsystem);
        Assert.NotEmpty(preview.AffectedTargets);
        Assert.True(preview.EstimatedImpactBytes > 0);
    }
}
