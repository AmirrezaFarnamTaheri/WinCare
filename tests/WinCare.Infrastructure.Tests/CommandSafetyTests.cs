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
}
