using WinCare.Application.Navigation;
using WinCare.Application.Tools;
using WinCare.CommandCatalog.Models;

namespace WinCare.Application.Tests;

public sealed class ToolCatalogServiceTests
{
    private readonly ToolCatalogService _service = new();

    [Fact]
    public void Empty_search_returns_all_commands()
    {
        Assert.Equal(259, _service.Search(string.Empty).Count);
    }

    [Theory]
    [InlineData("Windows Update", "wua-search")]
    [InlineData("quic", "quic-capability")]
    [InlineData("legacy-unsafe", "legacy-unsafe")]
    public void Search_matches_plain_language_and_command_ids(string query, string expectedId)
    {
        Assert.Contains(_service.Search(query), command => command.Id == expectedId);
    }

    [Fact]
    public void Read_only_filter_excludes_mutating_commands()
    {
        IReadOnlyList<CommandDefinition> commands = _service.Search(null, new ToolFilter(ReadOnly: true));

        Assert.NotEmpty(commands);
        Assert.All(commands, command => Assert.True(command.ReadOnly));
    }

    [Fact]
    public void Navigation_catalog_matches_the_approved_primary_structure()
    {
        string[] labels = NavigationCatalog.Items.Select(item => item.Label).ToArray();

        Assert.Equal(
            ["Home", "Checkup", "System care", "Security", "Repair & recovery", "All tools", "Activity", "Settings"],
            labels);
        Assert.Equal(["Commands", "Categories", "Favorites", "Recent", "Presets"],
            NavigationCatalog.Items.Single(item => item.Id == "all-tools").Tabs);
    }
}
