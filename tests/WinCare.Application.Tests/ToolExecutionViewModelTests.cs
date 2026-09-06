using WinCare.App.ViewModels.Pages;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Tests;

public sealed class ToolExecutionViewModelTests
{
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
    public async Task Moderate_tool_can_apply_after_lightweight_confirmation_without_preview()
    {
        var moderateDef = new CommandDefinition("moderate-change", "Moderate Change", "Moderate Change", "Area", "Section",
            CommandRisk.Moderate, false, AdministratorAccess.No, RestartExpectation.No,
            "test", MigrationStatus.Implemented, ["moderate"], RiskTier.Moderate);

        var handler = new DirectHandler("moderate-change");
        var viewModel = new ToolExecutionViewModel(new CommandDispatcher([moderateDef], [handler]), _ => { });
        viewModel.SelectTool(new ToolRowViewModel(moderateDef));

        Assert.True(viewModel.IsModerateTool);
        Assert.True(viewModel.RequiresApprovalSwitch);
        Assert.True(viewModel.CanApproveReview);

        viewModel.IsReviewApproved = true;
        Assert.True(viewModel.IsReviewApproved);
        Assert.Equal("Apply changes", viewModel.PrimaryActionLabel);

        await viewModel.ExecuteSelectedToolCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsExecutionSuccess);
        Assert.Equal(1, handler.CallCount);
        Assert.True(handler.LastWasApply);
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
        Assert.Equal("Preview Impact", viewModel.PrimaryActionLabel);

        await viewModel.ExecuteSelectedToolCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsExecutionSuccess);
        Assert.False(handler.LastWasApply);
        Assert.True(viewModel.CanApproveReview);

        viewModel.IsReviewApproved = true;
        Assert.Equal("Execute Destructive Action", viewModel.PrimaryActionLabel);

        await viewModel.ExecuteSelectedToolCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsExecutionSuccess);
        Assert.True(handler.LastWasApply);
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
}
