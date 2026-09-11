using WinCare.App.ViewModels.Pages;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Tests;

/// <summary>
/// Regression tests for F-007 (view-model level): raw JSON imports must update the typed
/// field values, and invalid advanced JSON must surface an error instead of being ignored.
/// The page-level control mounting is covered by the AllToolsPage Loaded retry.
/// </summary>
public sealed class ToolParameterRoundTripTests
{
    private static CommandDefinition CleanerDefinition() => new(
        "cleaner-disk-pressure", "Disk-pressure cleanup", "Clean temp files", "System care", "Clean up",
        CommandRisk.Moderate, false, AdministratorAccess.No, RestartExpectation.No,
        "test", MigrationStatus.Implemented, ["cleaner"], RiskTier.Moderate);

    [Fact]
    public void Typed_values_reach_the_raw_json_and_back()
    {
        var viewModel = new ToolExecutionViewModel(new CommandDispatcher([CleanerDefinition()], [new NoopHandler()]), _ => { });
        viewModel.SelectTool(new ToolRowViewModel(CleanerDefinition()));

        ToolParameterFieldViewModel days = Assert.Single(viewModel.ParameterFields, field => field.Name == "OlderThanDays");

        // Typed control edit flows into the model and the serialized JSON payload.
        days.Value = "30";
        viewModel.UseAdvancedParameterJson = true;
        Assert.Contains("\"OlderThanDays\": 30", viewModel.ParameterJson);

        // Editing raw JSON updates the model; switching back must refresh the typed field.
        viewModel.ParameterJson = """{"OlderThanDays": 45}""";
        viewModel.UseAdvancedParameterJson = false;
        Assert.Equal("45", days.Value);
    }

    [Fact]
    public void Invalid_advanced_json_is_surfaced_as_a_parameter_error()
    {
        var viewModel = new ToolExecutionViewModel(new CommandDispatcher([CleanerDefinition()], [new NoopHandler()]), _ => { });
        viewModel.SelectTool(new ToolRowViewModel(CleanerDefinition()));

        viewModel.UseAdvancedParameterJson = true;
        viewModel.ParameterJson = "{not valid json";
        viewModel.UseAdvancedParameterJson = false;

        Assert.True(viewModel.HasExecutionResult);
        Assert.True(viewModel.IsExecutionError);
        Assert.Contains("invalid", viewModel.ExecutionMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class NoopHandler : ICommandHandler
    {
        public string CommandId => "cleaner-disk-pressure";
        public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(CommandHandlerOutcome.Succeeded("cleaner-disk-pressure.ok", "ok"));
    }
}
