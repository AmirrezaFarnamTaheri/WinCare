using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Commands;

namespace WinCare.Application.Tests;

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
    public async Task WindowsCommandExecutor_UnhandledReadOnlyRoute_ReturnsBlockedFailClosed()
    {
        using WindowsCommandExecutor executor = new();
        // Create a dummy unhandled definition
        CommandDefinition unhandledDef = new(
            Id: "unhandled-test-route-12345",
            Title: "Unhandled Test Route",
            Summary: "Unhandled test route",
            Area: "System",
            Risk: "Read-only",
            AdministratorAccess: "Optional",
            Restart: "Never",
            Status: "Implemented",
            ReadOnly: true,
            DefaultParametersJson: "{}"
        );

        CommandRequest request = CommandRequest.Preview("unhandled-test-route-12345");
        CommandHandlerOutcome outcome = await executor.ExecuteAsync(unhandledDef, request, CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, outcome.Status);
        Assert.Equal("unhandled-test-route-12345.blocked", outcome.Code);
        Assert.Contains("No concrete native inspection route implemented", outcome.Message);
    }
}
