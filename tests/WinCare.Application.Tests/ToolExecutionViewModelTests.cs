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
            CommandRisk.Moderate, false, AdministratorAccess.No, RestartExpectation.No,
            "test", MigrationStatus.Implemented, []);
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
