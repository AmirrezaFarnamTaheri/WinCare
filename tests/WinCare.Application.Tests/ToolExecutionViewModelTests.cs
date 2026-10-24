using WinCare.App.ViewModels.Pages;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Tests;

public sealed class ToolExecutionViewModelTests
{
    [Fact]
    public void UnavailableToolExplainsWhyItCannotExecute()
    {
        CommandDefinition definition = WinCare.CommandCatalog.CommandCatalog.Find("storage-dedup-hardlink")!;
        var vm = new ToolExecutionViewModel(new CommandDispatcher([definition], []), _ => { });
        vm.SelectTool(new ToolRowViewModel(definition));
        Assert.False(vm.CanRunSelectedTool);
        Assert.False(vm.ExecuteSelectedToolCommand.CanExecute(null));
        Assert.Equal("Unavailable", vm.PrimaryActionLabel);
        Assert.Equal("This action is unavailable", vm.ActionFlowTitle);
        Assert.Contains("No changes will be made", vm.ActionFlowDescription);
    }

    [Fact]
    public void SuspendedToolIsMarkedUnavailableInListAndCannotAskForDestructiveApproval()
    {
        CommandDefinition definition = WinCare.CommandCatalog.CommandCatalog.Find("storage-dedup-hardlink")!;
        var row = new ToolRowViewModel(definition);
        Assert.False(row.IsAvailable);
        Assert.Equal("Unavailable", row.ImpactPillLabel);
        Assert.Equal("Unavailable", row.StatusPillLabel);
        Assert.Contains("unavailable", row.ToolAccessibleName, StringComparison.OrdinalIgnoreCase);

        var vm = new ToolExecutionViewModel(new CommandDispatcher([definition], []), _ => { });
        vm.SelectTool(row);
        Assert.True(vm.IsSelectedToolUnavailable);
        Assert.False(vm.ShowDestructiveWarning);
        Assert.False(vm.RequiresApprovalSwitch);
        Assert.False(vm.CanApproveReview);
    }

    [Fact]
    public async Task RecentHistoryPersistenceFailureNeverErasesSuccessfulExecution()
    {
        var definition = new CommandDefinition("safe-clean", "Safe Clean", "Safe Clean", "Area", "Section",
            CommandRisk.Low, false, AdministratorAccess.No, RestartExpectation.No,
            "test", MigrationStatus.Implemented, ["safe"], RiskTier.Safe);
        var handler = new DirectHandler(definition.Id);
        var vm = new ToolExecutionViewModel(
            new CommandDispatcher([definition], [handler]),
            _ => throw new IOException("Simulated history storage failure"));
        vm.SelectTool(new ToolRowViewModel(definition));

        await vm.ExecuteSelectedToolCommand.ExecuteAsync(null);

        Assert.Equal(1, handler.CallCount);
        Assert.True(handler.LastWasApply);
        Assert.True(vm.IsExecutionSuccess);
        Assert.Equal("Executed directly.", vm.ExecutionMessage);
    }

    [Fact]
    public async Task Cancellation_stays_pending_until_the_handler_finishes()
    {
        var definition = new CommandDefinition("test", "Test", "Test", "Test", "Test",
            CommandRisk.Low, true, AdministratorAccess.No, RestartExpectation.No,
            "test", MigrationStatus.Implemented, [], RiskTier.Safe);
        var handler = new PendingHandler();
        var vm = new ToolExecutionViewModel(new CommandDispatcher([definition], [handler]), _ => { });
        vm.SelectTool(new ToolRowViewModel(definition));
        Assert.False(vm.CancelSelectedToolCommand.CanExecute(null));
        Task execution = vm.ExecuteSelectedToolCommand.ExecuteAsync(null);
        await handler.Started.Task;

        vm.CancelSelectedToolCommand.Execute(null);

        Assert.True(vm.IsExecuting);
        Assert.True(vm.IsCancellationRequested);
        Assert.Equal("Stopping…", vm.CancelActionLabel);
        Assert.False(vm.CancelSelectedToolCommand.CanExecute(null));
        handler.Completion.SetResult(CommandHandlerOutcome.Succeeded("test.done", "Finished"));
        await execution;
        Assert.False(vm.IsExecuting);
        Assert.False(vm.IsCancellationRequested);
        Assert.False(vm.CancelSelectedToolCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Invalid_advanced_inputs_keep_the_raw_editor_open(string input)
    {
        var vm = new ToolExecutionViewModel(new CommandDispatcher([], []), _ => { });
        vm.UseAdvancedParameterJson = true;
        vm.ParameterJson = input;

        vm.UseAdvancedParameterJson = false;

        Assert.True(vm.UseAdvancedParameterJson);
        Assert.Equal(input, vm.ParameterJson);
        Assert.True(vm.IsExecutionError);
        Assert.False(vm.IsReviewApproved);
    }

    [Fact]
    public void Oversized_advanced_input_can_be_corrected_without_losing_editor_state()
    {
        var vm = new ToolExecutionViewModel(new CommandDispatcher([], []), _ => { });
        vm.UseAdvancedParameterJson = true;
        vm.ParameterJson = new string(' ', 1024 * 1024 + 1);
        vm.UseAdvancedParameterJson = false;
        Assert.True(vm.UseAdvancedParameterJson);
        Assert.Contains("1 MiB", vm.ExecutionMessage);

        vm.ParameterJson = "{}";
        vm.UseAdvancedParameterJson = false;
        Assert.False(vm.UseAdvancedParameterJson);
        Assert.False(vm.IsReviewApproved);
    }

    [Fact]
    public void Preset_details_follow_current_inputs_and_never_grant_approval()
    {
        CommandDefinition definition = WinCare.CommandCatalog.CommandCatalog.Load().Single(command => command.Id == "preset");
        var vm = new ToolExecutionViewModel(new CommandDispatcher([definition], [new DirectHandler("preset")]), _ => { });
        vm.SelectTool(new ToolRowViewModel(definition));
        var field = vm.ParameterFields.Single(item => item.Name == "PresetId");
        PresetDefinition preset = WinCare.CommandCatalog.RemediationCatalog.LoadPresets()[0];
        field.Value = preset.Id;
        Assert.Contains(preset.Title, vm.PresetContents);
        foreach (string id in preset.RuleIds)
            Assert.Contains(WinCare.CommandCatalog.RemediationCatalog.LoadRules().Single(rule => rule.Id == id).Title, vm.PresetContents);
        Assert.False(vm.CanApproveReview);
        vm.UseAdvancedParameterJson = true;
        vm.ParameterJson = "{\"PresetId\":\"missing\"}";
        Assert.Contains("not in the built-in catalog", vm.PresetContents);
        Assert.DoesNotContain(preset.Title, vm.PresetContents);
        vm.SelectTool(null);
        Assert.False(vm.IsPresetTool);
        Assert.Empty(vm.PresetContents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changed_selection_or_parameters_invalidate_inflight_preview(bool changeSelection)
    {
        var definition = new CommandDefinition("test", "Test", "Test", "Test", "Test",
            CommandRisk.Critical, false, AdministratorAccess.No, RestartExpectation.No,
            "test", MigrationStatus.Implemented, [], RiskTier.Destructive);
        var handler = new PendingHandler();
        var viewModel = new ToolExecutionViewModel(new CommandDispatcher([definition], [handler]), _ => { });
        var row = new ToolRowViewModel(definition);
        viewModel.SelectTool(row);
        Task execution = viewModel.ExecuteSelectedToolCommand.ExecuteAsync(null);
        await handler.Started.Task;
        if (changeSelection)
        {
            viewModel.SelectTool(null);
            viewModel.SelectTool(row);
        }
        else viewModel.ParameterJson = "{\"changed\":true}";
        handler.Completion.SetResult(CommandHandlerOutcome.Succeeded("test.preview", "Old preview"));
        await execution;
        Assert.False(viewModel.CanApproveReview);
        Assert.False(viewModel.HasExecutionResult);
        viewModel.IsReviewApproved = true;
        Assert.False(viewModel.IsReviewApproved);
    }

    [Fact]
    public async Task Safe_mutating_tool_executes_in_one_click_without_approval_switch()
    {
        var safeDef = new CommandDefinition("safe-clean", "Safe Clean", "Safe Clean", "Area", "Section",
            CommandRisk.Low, false, AdministratorAccess.No, RestartExpectation.No,
            "test", MigrationStatus.Implemented, ["safe"], RiskTier.Safe);

        var handler = new DirectHandler("safe-clean");
        var viewModel = new ToolExecutionViewModel(new CommandDispatcher([safeDef], [handler]), _ => { });
        var row = new ToolRowViewModel(safeDef);
        viewModel.SelectTool(row);

        Assert.True(viewModel.IsSafeTool);
        Assert.False(viewModel.RequiresApprovalSwitch);
        Assert.Equal("Run tool", viewModel.PrimaryActionLabel);

        await viewModel.ExecuteSelectedToolCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsExecutionSuccess);
        Assert.Equal(1, handler.CallCount);
        Assert.True(handler.LastWasApply);
    }

    [Fact]
    public async Task Moderate_tool_previews_and_consumes_one_use_review_approval()
    {
        var moderateDef = new CommandDefinition("moderate-change", "Moderate Change", "Moderate Change", "Area", "Section",
            CommandRisk.Moderate, false, AdministratorAccess.No, RestartExpectation.No,
            "test", MigrationStatus.Implemented, ["moderate"], RiskTier.Moderate);

        var handler = new DirectHandler("moderate-change");
        var viewModel = new ToolExecutionViewModel(new CommandDispatcher([moderateDef], [handler]), _ => { });
        viewModel.SelectTool(new ToolRowViewModel(moderateDef));

        Assert.True(viewModel.IsModerateTool);
        Assert.True(viewModel.RequiresApprovalSwitch);
        Assert.False(viewModel.CanApproveReview);
        Assert.Equal("Preview impact", viewModel.PrimaryActionLabel);

        await viewModel.ExecuteSelectedToolCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsExecutionSuccess);
        Assert.False(handler.LastWasApply);
        Assert.True(viewModel.CanApproveReview);

        viewModel.IsReviewApproved = true;
        Assert.Equal("Apply reviewed change", viewModel.PrimaryActionLabel);
        await viewModel.ExecuteSelectedToolCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsExecutionSuccess);
        Assert.Equal(2, handler.CallCount);
        Assert.True(handler.LastWasApply);
        Assert.False(viewModel.IsReviewApproved);
    }

    [Fact]
    public async Task Destructive_tool_enforces_two_phase_preview_and_approval()
    {
        var destDef = new CommandDefinition("dest-wipe", "Destructive Wipe", "Destructive Wipe", "Area", "Section",
            CommandRisk.Critical, false, AdministratorAccess.No, RestartExpectation.No,
            "test", MigrationStatus.Implemented, ["dest"], RiskTier.Destructive);

        var handler = new DirectHandler("dest-wipe");
        var viewModel = new ToolExecutionViewModel(new CommandDispatcher([destDef], [handler]), _ => { });
        var row = new ToolRowViewModel(destDef);
        viewModel.SelectTool(row);

        Assert.True(viewModel.IsDestructiveTool);
        Assert.True(viewModel.RequiresApprovalSwitch);
        Assert.False(viewModel.CanApproveReview);
        Assert.Equal("Preview impact", viewModel.PrimaryActionLabel);
        Assert.Equal("Preview before applying", viewModel.ActionFlowTitle);

        await viewModel.ExecuteSelectedToolCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsExecutionSuccess);
        Assert.False(handler.LastWasApply);
        Assert.True(viewModel.CanApproveReview);

        viewModel.IsReviewApproved = true;
        Assert.Equal("Apply destructive change", viewModel.PrimaryActionLabel);
        Assert.Equal("Reviewed and ready", viewModel.ActionFlowTitle);

        await viewModel.ExecuteSelectedToolCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsExecutionSuccess);
        Assert.True(handler.LastWasApply);
    }

    [Fact]
    public async Task Destructive_approval_surface_appears_only_after_a_successful_preview()
    {
        // Before a preview there is nothing to review, so the surface must not stack a
        // permanent-change warning and an approval switch on top of the preview button.
        var destDef = new CommandDefinition("dest-wipe", "Destructive Wipe", "Destructive Wipe", "Area", "Section",
            CommandRisk.Critical, false, AdministratorAccess.No, RestartExpectation.No,
            "test", MigrationStatus.Implemented, ["dest"], RiskTier.Destructive);

        var handler = new DirectHandler("dest-wipe");
        var viewModel = new ToolExecutionViewModel(new CommandDispatcher([destDef], [handler]), _ => { });
        viewModel.SelectTool(new ToolRowViewModel(destDef));

        Assert.True(viewModel.RequiresApprovalSwitch);
        Assert.False(viewModel.IsApprovalStage);
        Assert.False(viewModel.CanApproveReview);
        Assert.Equal("Preview before applying", viewModel.ActionFlowTitle);

        await viewModel.ExecuteSelectedToolCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsApprovalStage);
        Assert.True(viewModel.CanApproveReview);
        Assert.Equal("Approve to apply", viewModel.ActionFlowTitle);
        Assert.Contains("approval applies once", viewModel.ActionFlowDescription);

        // Editing the inputs invalidates the preview, so the approval surface goes away again.
        viewModel.ParameterJson = "{\"changed\":true}";
        Assert.False(viewModel.IsApprovalStage);
        Assert.False(viewModel.CanApproveReview);
        Assert.Equal("Preview before applying", viewModel.ActionFlowTitle);
    }

    [Fact]
    public async Task Failed_preview_does_not_open_the_approval_surface()
    {
        // A failed preview resolves no targets, so nothing may be approved on the strength of it.
        var destDef = new CommandDefinition("dest-wipe", "Destructive Wipe", "Destructive Wipe", "Area", "Section",
            CommandRisk.Critical, false, AdministratorAccess.No, RestartExpectation.No,
            "test", MigrationStatus.Implemented, ["dest"], RiskTier.Destructive);

        var handler = new FailingHandler("dest-wipe");
        var viewModel = new ToolExecutionViewModel(new CommandDispatcher([destDef], [handler]), _ => { });
        viewModel.SelectTool(new ToolRowViewModel(destDef));

        await viewModel.ExecuteSelectedToolCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsExecutionError);
        Assert.False(viewModel.IsApprovalStage);
        Assert.False(viewModel.CanApproveReview);
    }

    private sealed class DirectHandler(string commandId) : ICommandHandler
    {
        public string CommandId { get; } = commandId;
        public int CallCount { get; private set; }
        public bool LastWasApply { get; private set; }

        public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastWasApply = request.Apply;
            return Task.FromResult(CommandHandlerOutcome.Succeeded(
                $"{CommandId}.ok",
                request.Apply ? "Executed directly." : "Preview generated.",
                System.Text.Json.JsonSerializer.SerializeToElement(new { apply = request.Apply })));
        }
    }

    private sealed class PendingHandler : ICommandHandler
    {
        public string CommandId => "test";
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<CommandHandlerOutcome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            Started.SetResult();
            return Completion.Task;
        }
    }

    private sealed class FailingHandler(string commandId) : ICommandHandler
    {
        public string CommandId { get; } = commandId;

        public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(CommandHandlerOutcome.Failed("test.failed", "The preview could not resolve its targets."));
    }
}
