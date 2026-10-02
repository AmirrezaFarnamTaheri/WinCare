using System.Text.Json;
using WinCare.App.ViewModels.Pages;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Tests;

public sealed class PresentationRefinementTests
{
    [Theory]
    [InlineData("{\"PackageNames\":[\"Alpha\",\"Beta\"]}")]
    [InlineData("{\"PackageNames\":\"Alpha,Beta\"}")]
    public void Programmatic_lists_round_trip_without_json_punctuation(string json)
    {
        var vm = Create("appx-installed-remove");
        using var document = JsonDocument.Parse(json);
        vm.ApplyParameterValues(document.RootElement);
        AssertList(vm);
        vm.UseAdvancedParameterJson = false;
        AssertList(vm);
    }

    [Theory]
    [InlineData("{\"PackageNames\":[\"Alpha\",\"Beta\"]}")]
    [InlineData("{\"PackageNames\":\"Alpha,Beta\"}")]
    public void Advanced_import_handles_both_string_and_array_lists(string json)
    {
        var vm = Create("appx-installed-remove");
        vm.UseAdvancedParameterJson = true;
        vm.ParameterJson = json;
        vm.UseAdvancedParameterJson = false;
        Assert.False(vm.UseAdvancedParameterJson);
        AssertList(vm);
    }

    [Fact]
    public void Same_row_preserves_inputs_but_changed_definition_resets_them()
    {
        var vm = new ToolExecutionViewModel(new CommandDispatcher([], []), _ => { });
        var definition = Definition("appx-installed-remove");
        var row = new ToolRowViewModel(definition);
        vm.SelectTool(row);
        var field = Assert.Single(vm.ParameterFields);
        field.Value = "Alpha,Beta";
        vm.SelectTool(row);
        Assert.Same(field, Assert.Single(vm.ParameterFields));
        Assert.Equal("Alpha,Beta", field.Value);
        vm.SelectTool(new ToolRowViewModel(definition with { Summary = "Changed scope" }));
        Assert.NotSame(field, Assert.Single(vm.ParameterFields));
        Assert.False(vm.IsReviewApproved);
        Assert.False(vm.CanApproveReview);
    }

    private static void AssertList(ToolExecutionViewModel vm)
    {
        vm.UseAdvancedParameterJson = true;
        using var actual = JsonDocument.Parse(vm.ParameterJson);
        Assert.Equal(new[] { "Alpha", "Beta" }, actual.RootElement.GetProperty("PackageNames")
            .EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [Fact]
    public void SystemCarePageViewModel_selects_performance_section_properly()
    {
        var vm = new SystemCarePageViewModel();
        Assert.False(vm.IsPerformanceSection);

        vm.SelectSection(1);
        Assert.True(vm.IsPerformanceSection);

        vm.SelectSection(2);
        Assert.False(vm.IsPerformanceSection);
    }

    [Fact]
    public void ToolRowViewModel_exposes_accessible_status_pill_tokens()
    {
        var readOnlyCmd = new CommandDefinition("test-ro", "Read Only Tool", "Summary", "Area", "Section",
            CommandRisk.ReadOnly, true, AdministratorAccess.No, RestartExpectation.No, "legacy", MigrationStatus.BehaviorVerified, []);
        var roRow = new ToolRowViewModel(readOnlyCmd);
        Assert.Equal("Read-only", roRow.RiskPillLabel);
        Assert.Equal("PillReadOnlyBgBrush", roRow.StatusPillBackgroundResourceKey);

        var moderateCmd = new CommandDefinition("test-mod", "Moderate Tool", "Summary", "Area", "Section",
            CommandRisk.Moderate, false, AdministratorAccess.No, RestartExpectation.No, "legacy", MigrationStatus.BehaviorVerified, []);
        var modRow = new ToolRowViewModel(moderateCmd);
        Assert.Equal("Moderate", modRow.RiskPillLabel);
        Assert.Equal("PillElevatedBgBrush", modRow.StatusPillBackgroundResourceKey);

        var destructiveCmd = new CommandDefinition("test-dest", "Destructive Tool", "Summary", "Area", "Section",
            CommandRisk.High, false, AdministratorAccess.No, RestartExpectation.No, "legacy", MigrationStatus.BehaviorVerified, []);
        var destRow = new ToolRowViewModel(destructiveCmd);
        Assert.Equal("Destructive", destRow.RiskPillLabel);
        Assert.Equal("PillMutatingBgBrush", destRow.StatusPillBackgroundResourceKey);
    }

    private static ToolExecutionViewModel Create(string id)
    {
        var vm = new ToolExecutionViewModel(new CommandDispatcher([], []), _ => { });
        vm.SelectTool(new ToolRowViewModel(Definition(id)));
        return vm;
    }

    private static CommandDefinition Definition(string id) => new(id, "Test", "Test", "Test", "Test",
        CommandRisk.High, false, AdministratorAccess.No, RestartExpectation.No,
        "test", MigrationStatus.Implemented, []);
}
