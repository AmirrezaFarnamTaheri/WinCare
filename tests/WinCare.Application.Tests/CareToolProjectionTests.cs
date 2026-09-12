using WinCare.App.ViewModels.Pages;
using WinCare.Application.Tools;

namespace WinCare.Application.Tests;

public sealed class CareToolProjectionTests
{
    [Fact]
    public void Latest_activity_is_matched_by_command_and_never_becomes_current_health()
    {
        var catalog = new ToolCatalogService();
        var definition = catalog.All[0];
        var now = DateTimeOffset.UtcNow;
        var old = new WinCare.Domain.Activity.ActivityRecord(Guid.NewGuid(), definition.Id, definition.Title,
            WinCare.Domain.Activity.ActivityState.Completed, now.AddDays(-1), now.AddDays(-1), "Earlier result", false);
        var recent = old with { Id = Guid.NewGuid(), StartedAt = now, CompletedAt = null, State = WinCare.Domain.Activity.ActivityState.Running };
        var projections = CareAreaProjectionService.Project(catalog, definition.Id, [recent, old]);
        Assert.Same(recent, projections.Single(item => item.Command.Id == definition.Id).LatestActivity);
        Assert.All(projections.Where(item => item.Command.Id != definition.Id), item => Assert.Null(item.LatestActivity));
    }

    private sealed class CarePage : TabbedPageViewModel
    {
        public CarePage() : base([new PageSection("Tools", "No matching tools.", [])]) { }
    }

    [Fact]
    public void Projection_preserves_catalog_identity_and_compact_layout_without_running_tools()
    {
        var catalog = new ToolCatalogService();
        var page = new CarePage();
        page.SetCompactLayout(true);
        page.ShowTools(catalog, "security");

        Assert.NotEmpty(page.CurrentRows);
        Assert.Equal(catalog.Search("security").Select(command => command.Id).Order(), page.CurrentRows.Select(row => row.CommandId).Order());
        Assert.All(page.CurrentRows, row =>
        {
            Assert.True(row.IsCompact);
            Assert.False(row.HasAction);
            Assert.Contains("Administrator:", row.Detail);
        });
        page.SetCompactLayout(false);
        Assert.All(page.CurrentRows, row => Assert.False(row.IsCompact));

        page.ShowTools(catalog, "storage cleanup");
        Assert.Contains(page.CurrentRows, row =>
            row.Title.Contains("cleanup", StringComparison.OrdinalIgnoreCase));

        page.ShowTools(catalog, "no-such-command-9c261c");
        Assert.True(page.IsEmpty);
        Assert.Empty(page.CurrentRows);
    }

    [Theory]
    [InlineData(0, "storage-report")]
    [InlineData(0, "installer-cache-analysis")]
    [InlineData(2, "app-residual-discovery")]
    [InlineData(2, "winget-upgrade-inventory")]
    [InlineData(3, "winget-upgrade-inventory")]
    public void System_care_surfaces_converged_discovery_planes(int section, string commandId)
    {
        var page = new SystemCarePageViewModel();
        page.SelectSection(section);
        page.ShowTools(new ToolCatalogService(), page.ToolSearchQuery);
        Assert.Contains(page.CurrentRows, row => row.CommandId == commandId);
    }
}
