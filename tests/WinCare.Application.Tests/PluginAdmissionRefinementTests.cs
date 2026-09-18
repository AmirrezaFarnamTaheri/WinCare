using WinCare.Application.Commands;
using WinCare.Application.Plugins;
using WinCare.App.ViewModels.Pages;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Tests;

public sealed class PluginAdmissionRefinementTests
{
    [Theory]
    [InlineData("Low", false, RiskTier.Moderate)]
    [InlineData("Low", true, RiskTier.Safe)]
    [InlineData("ReadOnly", false, RiskTier.Safe)]
    [InlineData("High", false, RiskTier.Destructive)]
    public void Manifest_tier_preserves_read_only_and_stricter_risk(string risk, bool readOnly, RiskTier expected)
    {
        var tool = new PluginToolDefinition { Id = "plugin.sample", Risk = risk, ReadOnly = readOnly };
        Assert.Equal(expected, tool.ToCommandDefinition("sample").RiskTier);
    }

    [Fact]
    public async Task Registered_plugin_mutation_completes_normal_preview_approval_flow()
    {
        var definition = new PluginToolDefinition { Id = "plugin.sample", Title = "Sample" }
            .ToCommandDefinition("sample") with { ExplicitRiskTier = null };
        var dispatcher = new CommandDispatcher([], []);
        var directory = Path.Combine(Path.GetTempPath(), "WinCare-admission-" + Guid.NewGuid().ToString("N"));
        var host = new DefaultPluginHost(dispatcher, directory, directory);
        var handler = new RecordingHandler(definition.Id);
        Assert.True(host.RegisterCommand(definition, handler));
        var registered = Assert.Single(host.RegisteredCommands);
        Assert.Equal(RiskTier.Moderate, registered.RiskTier);
        var vm = new ToolExecutionViewModel(dispatcher, _ => { });
        vm.SelectTool(new ToolRowViewModel(registered));
        Assert.True(vm.RequiresApprovalSwitch);
        await vm.ExecuteSelectedToolCommand.ExecuteAsync(null);
        Assert.True(vm.IsExecutionSuccess);
        Assert.False(handler.LastWasApply);
        Assert.True(vm.CanApproveReview);
        vm.IsReviewApproved = true;
        await vm.ExecuteSelectedToolCommand.ExecuteAsync(null);
        Assert.True(vm.IsExecutionSuccess);
        Assert.True(handler.LastWasApply);
        Assert.Equal(2, handler.Calls);
    }

    private sealed class RecordingHandler(string commandId) : ICommandHandler
    {
        public string CommandId => commandId;
        public bool LastWasApply { get; private set; }
        public int Calls { get; private set; }
        public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            LastWasApply = request.Apply;
            return Task.FromResult(CommandHandlerOutcome.Succeeded("sample.ok", "Inert test operation."));
        }
    }
}
