using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;
using Xunit;

namespace WinCare.Application.Tests;

public sealed class RiskTierAdmissionTests
{
    private static CommandDefinition CreateDef(string id, RiskTier tier, bool readOnly = false) =>
        new(
            id,
            id,
            $"Test {id}",
            "Maintenance",
            "Cleanup",
            readOnly ? CommandRisk.ReadOnly : (tier == RiskTier.Destructive ? CommandRisk.Critical : CommandRisk.Low),
            readOnly,
            AdministratorAccess.No,
            RestartExpectation.No,
            "test",
            MigrationStatus.Implemented,
            [id],
            tier);

    private sealed class EchoHandler(string commandId) : ICommandHandler
    {
        public string CommandId { get; } = commandId;
        public int InvocationCount { get; private set; }

        public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult(CommandHandlerOutcome.Succeeded(
                $"{CommandId}.ok",
                request.Apply ? "Applied directly." : "Previewed.",
                JsonSerializer.SerializeToElement(new { applied = request.Apply })));
        }
    }

    [Fact]
    public async Task Safe_mutating_command_executes_directly_with_one_click()
    {
        // Tier 1 (Safe): No preview required, no ReviewApproved option required.
        CommandDefinition safeDef = CreateDef("safe-clean", RiskTier.Safe);
        EchoHandler handler = new("safe-clean");
        CommandDispatcher dispatcher = new([safeDef], [handler]);

        CommandRequest request = new(
            "safe-clean",
            JsonSerializer.SerializeToElement(new { target = "cache" }),
            Apply: true,
            Guid.NewGuid());

        // Call without ReviewApproved and without any ApprovedMutationPlan
        CommandResult result = await dispatcher.ExecuteAsync(
            request,
            new CommandExecutionOptions(ReviewApproved: false),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Equal("safe-clean.ok", result.Code);
        Assert.Equal(1, handler.InvocationCount);
    }

    [Fact]
    public async Task Moderate_mutating_command_requires_confirmation_but_no_preview_plan()
    {
        // Tier 2 (Moderate): Requires ReviewApproved = true, but DOES NOT require preflight preview plan.
        CommandDefinition modDef = CreateDef("mod-service", RiskTier.Moderate);
        EchoHandler handler = new("mod-service");
        CommandDispatcher dispatcher = new([modDef], [handler]);

        CommandRequest request = new(
            "mod-service",
            JsonSerializer.SerializeToElement(new { service = "wua" }),
            Apply: true,
            Guid.NewGuid());

        // 1. Blocked when ReviewApproved = false
        CommandResult blocked = await dispatcher.ExecuteAsync(
            request,
            new CommandExecutionOptions(ReviewApproved: false),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, blocked.Status);
        Assert.Equal("command.review_required", blocked.Code);
        Assert.Equal(0, handler.InvocationCount);

        // 2. Admitted when ReviewApproved = true (even without any preview plan)
        CommandResult admitted = await dispatcher.ExecuteAsync(
            request,
            new CommandExecutionOptions(ReviewApproved: true),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, admitted.Status);
        Assert.Equal(1, handler.InvocationCount);
    }

    [Fact]
    public async Task Destructive_command_strictly_requires_preview_plan_and_approval()
    {
        // Tier 3 (Destructive): Strictly requires preflight preview, SHA-256 digest token, and ReviewApproved = true.
        CommandDefinition destDef = CreateDef("dest-wipe", RiskTier.Destructive);
        EchoHandler handler = new("dest-wipe");
        CommandDispatcher dispatcher = new([destDef], [handler]);
        JsonElement parameters = JsonSerializer.SerializeToElement(new { partition = "C:" });

        // 1. Direct Apply without preview is blocked
        CommandRequest directApply = new("dest-wipe", parameters, Apply: true, Guid.NewGuid());
        CommandResult blocked = await dispatcher.ExecuteAsync(
            directApply,
            new CommandExecutionOptions(ReviewApproved: true),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, blocked.Status);
        Assert.Equal("command.approval_plan_invalid", blocked.Code);
        Assert.Equal(0, handler.InvocationCount);

        // 2. Preview pass issues plan
        CommandResult preview = await dispatcher.ExecuteAsync(
            CommandRequest.Preview("dest-wipe", parameters),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, preview.Status);
        Assert.NotNull(preview.ReviewPlan);
        Assert.Equal(1, handler.InvocationCount);

        // 3. Execution with issued plan and ReviewApproved = true succeeds
        CommandRequest validApply = CommandRequest.Execute("dest-wipe", parameters, preview.ReviewPlan);
        CommandResult executed = await dispatcher.ExecuteAsync(
            validApply,
            new CommandExecutionOptions(ReviewApproved: true),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, executed.Status);
        Assert.Equal(2, handler.InvocationCount);

        // 4. Replaying the consumed plan is blocked
        CommandResult replayed = await dispatcher.ExecuteAsync(
            validApply,
            new CommandExecutionOptions(ReviewApproved: true),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, replayed.Status);
        Assert.Equal("command.approval_plan_invalid", replayed.Code);
        Assert.Equal(2, handler.InvocationCount); // Not invoked again
    }
}
