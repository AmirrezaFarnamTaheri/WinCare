using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Commands;
using Xunit;

namespace WinCare.Infrastructure.Tests;

public sealed class CommandSafetyTests
{
    [Fact]
    public void CommandPlanAdmission_InvalidLateStep_FailsValidationUpfront()
    {
        // Step 1 is valid (system overview), Step 2 has an unknown command ID
        using JsonDocument planDoc = JsonDocument.Parse("""
            [
                { "commandId": "system", "parameters": {} },
                { "commandId": "nonexistent-command-xyz", "parameters": {} }
            ]
            """);

        var ex = Assert.Throws<CommandParameterException>(() =>
            WindowsCommandExecutor.ValidateCommandPlanSteps(planDoc.RootElement));

        Assert.Equal("Steps", ex.ParameterName);
        Assert.Contains("Unknown command 'nonexistent-command-xyz'", ex.Message);
    }

    [Fact]
    public void CommandPlanAdmission_RecursiveOrchestrationStep_BlockedUpfront()
    {
        // Step 1 attempts recursive run-automation
        using JsonDocument planDoc = JsonDocument.Parse("""
            [
                { "commandId": "run-automation", "parameters": { "Steps": [] } }
            ]
            """);

        var ex = Assert.Throws<CommandParameterException>(() =>
            WindowsCommandExecutor.ValidateCommandPlanSteps(planDoc.RootElement));

        Assert.Equal("Steps", ex.ParameterName);
        Assert.Contains("Recursive orchestration command 'run-automation' is not allowed", ex.Message);
    }

    [Fact]
    public void CommandPlanAdmission_InvalidStepParameters_FailsValidationUpfront()
    {
        // Step 1 is valid, Step 2 is missing required Parameter 'Path' for sysmon-configure
        using JsonDocument planDoc = JsonDocument.Parse("""
            [
                { "commandId": "system", "parameters": {} },
                { "commandId": "sysmon-configure", "parameters": {} }
            ]
            """);

        var ex = Assert.Throws<CommandParameterException>(() =>
            WindowsCommandExecutor.ValidateCommandPlanSteps(planDoc.RootElement));

        Assert.Equal("Path", ex.ParameterName);
    }

    [Fact]
    public void CommandPlanAdmission_ReadOnlyStepMissingRequiredParameters_FailsValidationUpfront()
    {
        // process-modules is read-only but requires positive ProcessId parameter
        using JsonDocument planDoc = JsonDocument.Parse("""
            [
                { "commandId": "system", "parameters": {} },
                { "commandId": "process-modules", "parameters": { "ProcessId": 0 } }
            ]
            """);

        var ex = Assert.Throws<CommandParameterException>(() =>
            WindowsCommandExecutor.ValidateCommandPlanSteps(planDoc.RootElement));

        Assert.Equal("ProcessId", ex.ParameterName);
    }

    [Fact]
    public async Task WindowsCommandExecutor_UnhandledReadOnlyRoute_ReturnsBlockedFailClosed()
    {
        using WindowsCommandExecutor executor = new();
        CommandDefinition unhandledDef = new(
            Id: "unhandled-test-route-12345",
            Title: "Unhandled Test Route",
            Summary: "Unhandled test route",
            Area: "System",
            Section: "General",
            Risk: CommandRisk.ReadOnly,
            ReadOnly: true,
            AdministratorAccess: AdministratorAccess.No,
            Restart: RestartExpectation.No,
            LegacySource: "test.ps1",
            MigrationStatus: MigrationStatus.Implemented,
            Keywords: Array.Empty<string>()
        );

        CommandRequest request = CommandRequest.Preview("unhandled-test-route-12345");
        CommandHandlerOutcome outcome = await executor.ExecuteAsync(unhandledDef, request, CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, outcome.Status);
        Assert.Equal("unhandled-test-route-12345.blocked", outcome.Code);
        Assert.Contains("No concrete native inspection route implemented", outcome.Message);
    }

    [Fact]
    public async Task CommandStateStore_AtomicUpdateAsync_TransformsStateAtomically()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "WinCareStateTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            CommandStateStore store = new(testRoot);
            JsonElement fallback = JsonSerializer.SerializeToElement(new { count = 0 });

            JsonElement updated = await store.UpdateAsync("counter", fallback, current =>
            {
                int currentCount = current.GetProperty("count").GetInt32();
                return JsonSerializer.SerializeToElement(new { count = currentCount + 1 });
            }, CancellationToken.None);

            Assert.Equal(1, updated.GetProperty("count").GetInt32());

            JsonElement read = await store.ReadObjectAsync("counter", CancellationToken.None);
            Assert.Equal(1, read.GetProperty("count").GetInt32());
        }
        finally
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task CommandStateStore_ConcurrentUpdates_ZeroLostUpdates()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "WinCareConcurrentTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            CommandStateStore store = new(testRoot);
            JsonElement fallback = JsonSerializer.SerializeToElement(new { count = 0 });

            Task[] tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
            {
                await store.UpdateAsync("counter", fallback, current =>
                {
                    int currentCount = current.GetProperty("count").GetInt32();
                    return JsonSerializer.SerializeToElement(new { count = currentCount + 1 });
                }, CancellationToken.None);
            })).ToArray();

            await Task.WhenAll(tasks);

            JsonElement finalState = await store.ReadObjectAsync("counter", CancellationToken.None);
            Assert.Equal(50, finalState.GetProperty("count").GetInt32());
        }
        finally
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public void ApprovedMutationPlan_CanonicalHashing_OrderInsensitive()
    {
        using JsonDocument json1 = JsonDocument.Parse("""{"b": 2, "a": 1}""");
        using JsonDocument json2 = JsonDocument.Parse("""{"a": 1, "b": 2}""");

        ApprovedMutationPlan plan1 = ApprovedMutationPlan.Create("test-cmd", json1.RootElement);
        ApprovedMutationPlan plan2 = ApprovedMutationPlan.Create("test-cmd", json2.RootElement);

        Assert.Equal(plan1.ParametersDigest, plan2.ParametersDigest);
        Assert.True(plan1.IsValid("test-cmd", json2.RootElement));
    }

    [Fact]
    public void ApprovedMutationPlan_MismatchingParametersOrId_FailsValidation()
    {
        using JsonDocument json1 = JsonDocument.Parse("""{"a": 1}""");
        using JsonDocument json2 = JsonDocument.Parse("""{"a": 2}""");

        ApprovedMutationPlan plan = ApprovedMutationPlan.Create("test-cmd", json1.RootElement);

        Assert.False(plan.IsValid("different-cmd", json1.RootElement));
        Assert.False(plan.IsValid("test-cmd", json2.RootElement));
    }

    [Fact]
    public void CommandValidation_SpriteLayout_ValidatesNumericalDimensionsWithoutPath()
    {
        using JsonDocument validParamsDoc = JsonDocument.Parse("""
            { "FrameWidth": 32, "FrameHeight": 32, "SheetWidth": 256, "SheetHeight": 256 }
            """);
        CommandParameters validParams = new(validParamsDoc.RootElement);

        CommandDefinition spriteDef = new(
            Id: "experience-sprite-layout",
            Title: "Sprite Layout",
            Summary: "Calculate sprite layout",
            Area: "Experience",
            Section: "Visuals",
            Risk: CommandRisk.ReadOnly,
            ReadOnly: true,
            AdministratorAccess: AdministratorAccess.No,
            Restart: RestartExpectation.No,
            LegacySource: "Visuals.ps1",
            MigrationStatus: MigrationStatus.Implemented,
            Keywords: Array.Empty<string>()
        );

        // Should not throw CommandParameterException requiring "Path"
        WindowsCommandExecutor.ValidateCommandParameters(spriteDef, validParams);
    }

    [Fact]
    public async Task WindowsCommandExecutor_MutationPreview_ReturnsConcreteAffectedResources()
    {
        using WindowsCommandExecutor executor = new();
        CommandDefinition pagefileDef = new(
            Id: "pagefile-set",
            Title: "Configure Pagefile",
            Summary: "Configure pagefile settings",
            Area: "System",
            Section: "Memory",
            Risk: CommandRisk.Low,
            ReadOnly: false,
            AdministratorAccess: AdministratorAccess.Required,
            Restart: RestartExpectation.No,
            LegacySource: "Memory.ps1",
            MigrationStatus: MigrationStatus.Implemented,
            Keywords: Array.Empty<string>()
        );

        using JsonDocument paramsDoc = JsonDocument.Parse("""{ "Mode": "Automatic" }""");
        CommandRequest previewRequest = CommandRequest.Preview("pagefile-set", paramsDoc.RootElement);

        CommandHandlerOutcome outcome = await executor.ExecuteAsync(pagefileDef, previewRequest, CancellationToken.None);

        Assert.NotNull(outcome.Data);
        Assert.True(outcome.Data.Value.TryGetProperty("affectedResources", out JsonElement affected));
        Assert.Equal(JsonValueKind.Array, affected.ValueKind);
        Assert.True(affected.GetArrayLength() > 0);
        Assert.Equal("Registry/WMI", affected[0].GetProperty("resourceType").GetString());
    }

    [Fact]
    public async Task CommandDispatcher_MutatingCommand_WithoutApprovedMutationPlan_IsBlocked()
    {
        CommandDefinition pagefileDef = new(
            Id: "pagefile-set",
            Title: "Configure Pagefile",
            Summary: "Configure pagefile settings",
            Area: "System",
            Section: "Memory",
            Risk: CommandRisk.Low,
            ReadOnly: false,
            AdministratorAccess: AdministratorAccess.Required,
            Restart: RestartExpectation.No,
            LegacySource: "Memory.ps1",
            MigrationStatus: MigrationStatus.Implemented,
            Keywords: Array.Empty<string>()
        );

        using WindowsCommandExecutor executor = new();
        ICommandHandler handler = new DelegatingCommandHandler(pagefileDef, executor);
        CommandDispatcher dispatcher = new(new[] { pagefileDef }, new[] { handler });

        using JsonDocument paramsDoc = JsonDocument.Parse("""{ "Mode": "Automatic" }""");
        CommandRequest requestWithoutPlan = CommandRequest.Execute("pagefile-set", paramsDoc.RootElement, approval: null);

        CommandResult result = await dispatcher.ExecuteAsync(requestWithoutPlan, new CommandExecutionOptions(ReviewApproved: true), CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, result.Status);
        Assert.Equal("command.approval_plan_invalid", result.Code);
        Assert.Contains("requires a valid ApprovedMutationPlan", result.Message);
    }

    [Fact]
    public async Task CommandDispatcher_PreviewApproveApply_SucceedsWithValidCorrelationId()
    {
        CommandDefinition pagefileDef = new(
            Id: "pagefile-set",
            Title: "Configure Pagefile",
            Summary: "Configure pagefile settings",
            Area: "System",
            Section: "Memory",
            Risk: CommandRisk.Low,
            ReadOnly: false,
            AdministratorAccess: AdministratorAccess.Required,
            Restart: RestartExpectation.No,
            LegacySource: "Memory.ps1",
            MigrationStatus: MigrationStatus.Implemented,
            Keywords: Array.Empty<string>()
        );

        using WindowsCommandExecutor executor = new();
        ICommandHandler handler = new DelegatingCommandHandler(pagefileDef, executor);
        CommandDispatcher dispatcher = new(new[] { pagefileDef }, new[] { handler });

        using JsonDocument paramsDoc = JsonDocument.Parse("""{ "Mode": "Automatic" }""");
        CommandRequest previewReq = CommandRequest.Preview("pagefile-set", paramsDoc.RootElement);

        CommandResult previewResult = await dispatcher.ExecuteAsync(previewReq, CommandExecutionOptions.Default, CancellationToken.None);
        Assert.Equal(CommandResultStatus.Succeeded, previewResult.Status);

        ApprovedMutationPlan approval = ApprovedMutationPlan.Create(
            previewReq.CommandId,
            previewReq.Parameters,
            previewReq.CorrelationId);

        CommandRequest applyReq = CommandRequest.Execute(previewReq.CommandId, previewReq.Parameters, approval);
        CommandResult applyResult = await dispatcher.ExecuteAsync(applyReq, new CommandExecutionOptions(ReviewApproved: true), CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, applyResult.Status);
    }
}
