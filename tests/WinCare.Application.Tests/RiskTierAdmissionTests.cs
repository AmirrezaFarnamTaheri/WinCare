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
    public async Task Moderate_mutating_command_requires_confirmation_and_single_use_receipt()
    {
        // Tier 2 (Moderate) under the F-004 unified contract: requires ReviewApproved = true
        // AND a single-use review plan issued by a successful preview, exactly like the
        // Destructive tier, so every applied change has reviewed targets and a receipt.
        CommandDefinition modDef = CreateDef("mod-service", RiskTier.Moderate);
        EchoHandler handler = new("mod-service");
        CommandDispatcher dispatcher = new([modDef], [handler]);
        JsonElement parameters = JsonSerializer.SerializeToElement(new { service = "wua" });

        // 1. Blocked when ReviewApproved = false
        CommandResult blocked = await dispatcher.ExecuteAsync(
            new CommandRequest("mod-service", parameters, Apply: true, Guid.NewGuid()),
            new CommandExecutionOptions(ReviewApproved: false),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, blocked.Status);
        Assert.Equal("command.review_required", blocked.Code);
        Assert.Equal(0, handler.InvocationCount);

        // 2. Blocked when ReviewApproved = true but no review plan was supplied
        CommandResult unreviewed = await dispatcher.ExecuteAsync(
            new CommandRequest("mod-service", parameters, Apply: true, Guid.NewGuid()),
            new CommandExecutionOptions(ReviewApproved: true),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, unreviewed.Status);
        Assert.Equal("command.approval_plan_invalid", unreviewed.Code);
        Assert.Equal(0, handler.InvocationCount);

        // 3. Preview pass issues the plan
        CommandResult preview = await dispatcher.ExecuteAsync(
            CommandRequest.Preview("mod-service", parameters),
            CommandExecutionOptions.Default,
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, preview.Status);
        Assert.NotNull(preview.ReviewPlan);
        Assert.Equal(1, handler.InvocationCount); // Preview reached the handler; nothing applied

        // 4. Execution with the issued plan and ReviewApproved = true succeeds
        CommandResult executed = await dispatcher.ExecuteAsync(
            CommandRequest.Execute("mod-service", parameters, preview.ReviewPlan),
            new CommandExecutionOptions(ReviewApproved: true),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, executed.Status);
        Assert.Equal(2, handler.InvocationCount);

        // 5. Replaying the consumed plan is blocked
        CommandResult replayed = await dispatcher.ExecuteAsync(
            CommandRequest.Execute("mod-service", parameters, preview.ReviewPlan),
            new CommandExecutionOptions(ReviewApproved: true),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, replayed.Status);
        Assert.Equal("command.approval_plan_invalid", replayed.Code);
        Assert.Equal(2, handler.InvocationCount);
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

    [Fact]
    public void Explicit_safe_tier_cannot_downgrade_a_destructive_command()
    {
        CommandDefinition definition = new(
            "critical-clean",
            "critical-clean",
            "Critical cleanup",
            "Maintenance",
            "Cleanup",
            CommandRisk.Critical,
            ReadOnly: false,
            AdministratorAccess.Required,
            RestartExpectation.No,
            "test",
            MigrationStatus.Implemented,
            ["critical-clean"],
            RiskTier.Safe);

        Assert.Equal(RiskTier.Destructive, definition.RiskTier);
    }

    [Fact]
    public void Cleaner_commands_are_no_longer_downgraded_to_safe_by_their_identifier()
    {
        // F-004: cleaner-disk-pressure is declared Moderate and must derive Moderate,
        // so quick-clean flows cannot bypass preview/review admission.
        CommandDefinition? cleaner = WinCare.CommandCatalog.CommandCatalog.Find("cleaner-disk-pressure");
        Assert.NotNull(cleaner);
        Assert.Equal(RiskTier.Moderate, cleaner.RiskTier);
    }

    public static TheoryData<RiskTier, bool, bool, CommandResultStatus, string> MutationAdmissionMatrix => new()
    {
        // (tier, ReviewApproved, planSupplied, expectedStatus, expectedCode)
        { RiskTier.Safe, false, false, CommandResultStatus.Succeeded, "safe.ok" },
        { RiskTier.Safe, true, false, CommandResultStatus.Succeeded, "safe.ok" },
        { RiskTier.Moderate, false, false, CommandResultStatus.Blocked, "command.review_required" },
        { RiskTier.Moderate, true, false, CommandResultStatus.Blocked, "command.approval_plan_invalid" },
        { RiskTier.Destructive, false, false, CommandResultStatus.Blocked, "command.review_required" },
        { RiskTier.Destructive, true, false, CommandResultStatus.Blocked, "command.approval_plan_invalid" },
    };

    [Theory]
    [MemberData(nameof(MutationAdmissionMatrix))]
    public async Task Mutation_admission_matrix_matches_the_single_contract(
        RiskTier tier,
        bool reviewApproved,
        bool planSupplied,
        CommandResultStatus expectedStatus,
        string expectedCodeFragment)
    {
        string id = tier switch
        {
            RiskTier.Safe => "safe",
            RiskTier.Moderate => "moderate",
            _ => "destructive",
        };
        CommandDefinition definition = CreateDef(id, tier);
        EchoHandler handler = new(id);
        CommandDispatcher dispatcher = new([definition], [handler]);
        JsonElement parameters = JsonSerializer.SerializeToElement(new { target = "fixture" });

        CommandRequest request = planSupplied
            ? CommandRequest.Execute(id, parameters, await ApprovedPlanForAsync(dispatcher, definition, parameters))
            : new CommandRequest(id, parameters, Apply: true, Guid.NewGuid());

        CommandResult result = await dispatcher.ExecuteAsync(
            request,
            new CommandExecutionOptions(ReviewApproved: reviewApproved),
            CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedCodeFragment, result.Code);
        int expectedInvocations = expectedStatus == CommandResultStatus.Succeeded ? 1 : 0;
        Assert.Equal(expectedInvocations, handler.InvocationCount);
    }

    private static async Task<ApprovedMutationPlan> ApprovedPlanForAsync(
        CommandDispatcher dispatcher, CommandDefinition definition, JsonElement parameters)
    {
        CommandResult preview = await dispatcher.ExecuteAsync(
            CommandRequest.Preview(definition.Id, parameters),
            CommandExecutionOptions.Default,
            CancellationToken.None);
        Assert.NotNull(preview.ReviewPlan);
        return preview.ReviewPlan;
    }

    [Fact]
    public async Task Unsupported_dynamic_risk_tier_is_blocked_before_handler_execution()
    {
        const string commandId = "plugin-invalid-risk";
        CommandDefinition definition = new(
            commandId,
            commandId,
            "Dynamic command with invalid risk metadata",
            "Plugin",
            "Test",
            CommandRisk.Low,
            ReadOnly: false,
            AdministratorAccess.No,
            RestartExpectation.No,
            "test",
            MigrationStatus.Implemented,
            [commandId],
            (RiskTier)99);
        EchoHandler handler = new(commandId);
        CommandDispatcher dispatcher = new([], []);

        Assert.True(dispatcher.RegisterDynamicCommand(definition, handler));

        CommandResult result = await dispatcher.ExecuteAsync(
            new CommandRequest(
                commandId,
                JsonSerializer.SerializeToElement(new { target = "cache" }),
                Apply: true,
                Guid.NewGuid()),
            new CommandExecutionOptions(ReviewApproved: true),
            CancellationToken.None);

        Assert.Equal(CommandResultStatus.Blocked, result.Status);
        Assert.Equal("command.risk_tier_invalid", result.Code);
        Assert.Equal(0, handler.InvocationCount);
    }
}
