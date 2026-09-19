using System.Collections.Generic;
using System.Linq;
using WinCare.Application.Navigation;
using WinCare.Application.Plugins;
using WinCare.Application.Tools;
using WinCare.CommandCatalog.Models;
using Xunit;

namespace WinCare.Application.Tests;

public sealed class GlobalSearchServiceTests
{
    private static CommandDefinition Cmd(string id, string title, string summary = "", string area = "Utilities", string section = "General", string[]? keywords = null) =>
        new(id, title, summary, area, section, CommandRisk.Low, true, AdministratorAccess.No, RestartExpectation.No, "core", MigrationStatus.BehaviorVerified, keywords ?? []);

    private static PluginRegistryEntry Extension(string id, string name, string description = "", string category = "Utility", string author = "acme") =>
        new(id, name, "1.0.0", author, description, category, @"C:\plugins\" + id, false, PluginState.Enabled, [], null);
    private static GlobalSearchService Service(IReadOnlyList<CommandDefinition>? tools = null, IReadOnlyList<PluginRegistryEntry>? extensions = null) =>
        new(new ToolCatalogService(tools ?? []), new StubPluginRegistry(extensions ?? []));

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("", "missing")]
    public void ScoreQuery_is_zero_for_empty_or_partially_matching_queries(string query, string? secondToken)
    {
        string text = secondToken is null ? query : $"{query} {secondToken}";
        Assert.Equal(0, GlobalSearchService.ScoreQuery(text, "anything at all"));
    }

    [Fact]
    public void ScoreQuery_uses_best_field_match_per_token_and_is_case_insensitive()
    {
        // Exact (100) beats prefix (70) beats contains (35); per-token scores sum.
        Assert.Equal(100, GlobalSearchService.ScoreQuery("quic", "QUIC"));
        Assert.Equal(70, GlobalSearchService.ScoreQuery("qui", "quic-capability"));
        Assert.Equal(35, GlobalSearchService.ScoreQuery("capability", "quic-capability"));
        Assert.Equal(170, GlobalSearchService.ScoreQuery("quic cap", "quic-capability", "cap"));
        // Two fields, one token: the best match across fields wins, no summing per field.
        Assert.Equal(100, GlobalSearchService.ScoreQuery("cap", "networking", "cap"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Search_returns_nothing_for_blank_text(string? text) => Assert.Empty(Service().Search(text));

    [Fact]
    public void Search_builds_page_tool_extension_and_help_suggestions_with_correct_routes()
    {
        var service = Service(
            [Cmd("wua-search", "Windows Update", "search for pending updates")],
            [Extension("acme.toolkit", "Acme Toolkit", "handy helpers")]);

        IReadOnlyList<GlobalSearchSuggestion> tool = service.Search("windows");
        Assert.Single(tool);
        Assert.Equal(GlobalSearchSuggestionKind.Tool, tool[0].Kind);
        Assert.Equal("all-tools", tool[0].Route);
        Assert.Equal("wua-search", tool[0].Query);

        IReadOnlyList<GlobalSearchSuggestion> page = service.Search("Checkup");
        Assert.Single(page);
        Assert.Equal(GlobalSearchSuggestionKind.Page, page[0].Kind);
        Assert.Equal("checkup", page[0].Route);
        Assert.Null(page[0].Query);

        IReadOnlyList<GlobalSearchSuggestion> extension = service.Search("Toolkit");
        Assert.Single(extension);
        Assert.Equal(GlobalSearchSuggestionKind.Extension, extension[0].Kind);
        Assert.Equal("plugin-store", extension[0].Route);
        Assert.Equal("Acme Toolkit", extension[0].Query);

        IReadOnlyList<GlobalSearchSuggestion> help = service.Search("shortcuts");
        Assert.Contains(help, item => item.Kind == GlobalSearchSuggestionKind.Help &&
            item.Title == "Keyboard shortcuts" && item.Route == "help");
    }

    [Fact]
    public void Search_ranks_strong_tool_match_above_weak_page_matches()
    {
        // Token "before": page tabs match weakly (Help tab "Before changes" contains it,
        // 35 + 30 page boost = 65); the tool title matches exactly (100).
        var service = Service([Cmd("preview-before", "Before")]);

        IReadOnlyList<GlobalSearchSuggestion> results = service.Search("before");

        Assert.Equal(GlobalSearchSuggestionKind.Tool, results[0].Kind);
        Assert.Contains(results, item => item.Kind == GlobalSearchSuggestionKind.Page);
        Assert.DoesNotContain(results, item => item.Kind == GlobalSearchSuggestionKind.Help);
    }

    [Fact]
    public void Search_ranks_page_above_extension_via_page_boost()
    {
        // Token "media": the "Repair & recovery" page matches via its joined tabs field
        // "…Reset & media…" (contains = 35, +30 page boost = 65); the extension contains
        // the token only mid-string in its description (35 + 10 extension boost = 45).
        // Note the "Extensions" page also matches — "media" is inside "extensions" — so
        // compare first-occurrence positions rather than assuming a single page hit.
        var service = Service(extensions: [Extension("acme.hub", "Podcast Hub", "streaming media offline")]);

        IReadOnlyList<GlobalSearchSuggestion> results = service.Search("media");

        Assert.Contains(results, item => item.Kind == GlobalSearchSuggestionKind.Page && item.Route == "repair-recovery");
        List<GlobalSearchSuggestion> ordered = results.ToList();
        Assert.True(ordered.FindIndex(item => item.Route == "repair-recovery") >= 0
                    && ordered.FindIndex(item => item.Route == "repair-recovery") <
                    ordered.FindIndex(item => item.Kind == GlobalSearchSuggestionKind.Extension));
    }

    [Fact]
    public void Search_deduplicates_and_caps_results_at_ten()
    {
        // 20 commands collapse to one distinct (title, route, query) tuple...
        var commands = new List<CommandDefinition>();
        for (int i = 0; i < 20; i++)
        {
            commands.Add(Cmd("bulk-x", "Bulk", $"entry {i}"));
        }

        IReadOnlyList<GlobalSearchSuggestion> deduped = Service(commands).Search("Bulk");
        Assert.Single(deduped);

        // ...while 20 distinct tools are capped at ten.
        var distinct = new List<CommandDefinition>();
        for (int i = 0; i < 20; i++)
        {
            distinct.Add(Cmd($"bulk-{i}", "Bulk", $"entry {i}"));
        }

        IReadOnlyList<GlobalSearchSuggestion> capped = Service(distinct).Search("Bulk");
        Assert.Equal(10, capped.Count);
    }

    private sealed class StubPluginRegistry(IReadOnlyList<PluginRegistryEntry> entries) : IPluginRegistry
    {
        public event System.EventHandler? RegistryChanged { add { } remove { } }

        public Task DiscoverAndInitializeAsync(IPluginHost host, CancellationToken ct = default) => Task.CompletedTask;
        public IReadOnlyList<PluginRegistryEntry> GetAllPlugins() => entries;
        public IReadOnlyList<CommandDefinition> GetActivePluginCommands() => [];
        public IReadOnlyList<IPluginWidget> GetActivePluginWidgets() => [];
        public Task EnablePluginAsync(string pluginId, IPluginHost host, CancellationToken ct = default) => Task.CompletedTask;
        public Task DisablePluginAsync(string pluginId, IPluginHost host, CancellationToken ct = default) => Task.CompletedTask;
    }
}
