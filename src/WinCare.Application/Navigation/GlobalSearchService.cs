using WinCare.Application.Plugins;
using WinCare.Application.Tools;
using WinCare.CommandCatalog.Models;

namespace WinCare.Application.Navigation;

/// <summary>
/// Pure ranking engine behind the shell's global search box. Suggestions carry route keys that
/// must match the routing table, so construction resolves the fixed routes once — a catalog
/// rename fails at startup instead of producing suggestions to nowhere.
/// </summary>
public sealed class GlobalSearchService
{
    private const int PageBoost = 30;
    private const int ExtensionBoost = 10;
    private const int HelpBoost = 5;
    private const int MaxResults = 10;

    private static readonly (string Title, string Terms, string Route)[] HelpTopics =
    [
        ("How changes work", "changes confirmation preview risk destructive", "help"),
        ("Keyboard shortcuts", "keyboard shortcut ctrl k ctrl f search", "help"),
        ("Find a tool", "find discover tool category power tools", "help"),
        ("Recent activity and results", "history report recent activity results", "activity"),
        ("About WinCare", "about version license credits", "about"),
    ];

    private readonly ToolCatalogService _tools;
    private readonly IPluginRegistry _extensions;
    private readonly string _allToolsRoute;
    private readonly string _pluginStoreRoute;
    private readonly string _helpRoute;
    private readonly string _activityRoute;
    private readonly string _aboutRoute;

    public GlobalSearchService(ToolCatalogService tools, IPluginRegistry extensions)
    {
        _tools = tools;
        _extensions = extensions;
        _allToolsRoute = RouteFor("all-tools");
        _pluginStoreRoute = RouteFor("plugin-store");
        _helpRoute = RouteFor("help");
        _activityRoute = RouteFor("activity");
        _aboutRoute = RouteFor("about");
    }

    private static string RouteFor(string id) =>
        NavigationCatalog.Items.Single(item => item.Id == id).Id;

    public IReadOnlyList<GlobalSearchSuggestion> Search(string? text)
    {
        string query = text?.Trim() ?? string.Empty;
        if (query.Length == 0) return [];

        var candidates = new List<(GlobalSearchSuggestion Item, int Score)>();
        foreach (NavigationDefinition route in NavigationCatalog.Items)
        {
            int score = ScoreQuery(query, route.Label, route.Id, string.Join(' ', route.Tabs));
            if (score > 0)
            {
                candidates.Add((new GlobalSearchSuggestion(route.Label, route.IsHidden ? "WinCare information" : "Open this area", route.Id, null, GlobalSearchSuggestionKind.Page), score + PageBoost));
            }
        }

        foreach (CommandDefinition tool in _tools.All)
        {
            int score = ScoreQuery(query, tool.Title, tool.Summary, tool.Area, tool.Section, tool.Id, string.Join(' ', tool.Keywords));
            if (score <= 0) continue;
            candidates.Add((new GlobalSearchSuggestion(tool.Title, $"{tool.Area} · {tool.Section}", _allToolsRoute, tool.Id, GlobalSearchSuggestionKind.Tool), score));
        }

        foreach (PluginRegistryEntry extension in _extensions.GetAllPlugins())
        {
            int score = ScoreQuery(query, extension.Name, extension.Description, extension.Category, extension.Author, extension.Id);
            if (score <= 0) continue;
            candidates.Add((new GlobalSearchSuggestion(extension.Name, $"Extension · {extension.Category}", _pluginStoreRoute, extension.Name, GlobalSearchSuggestionKind.Extension), score + ExtensionBoost));
        }

        foreach ((string title, string terms, string routeKey) in HelpTopics)
        {
            int score = ScoreQuery(query, title, terms);
            if (score <= 0) continue;
            string route = routeKey switch
            {
                "help" => _helpRoute,
                "activity" => _activityRoute,
                "about" => _aboutRoute,
                _ => routeKey,
            };
            candidates.Add((new GlobalSearchSuggestion(title, "Help topic", route, null, GlobalSearchSuggestionKind.Help), score + HelpBoost));
        }

        return candidates.OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Item.Title, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Item)
            .DistinctBy(item => (item.Title, item.Route, item.Query))
            .Take(MaxResults)
            .ToArray();
    }

    /// <summary>Per-token best-field score; every token must match some field or the row scores 0.</summary>
    public static int ScoreQuery(string query, params string?[] fields)
    {
        string[] tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return 0;

        int score = 0;
        foreach (string token in tokens)
        {
            int tokenScore = 0;
            foreach (string? field in fields)
            {
                if (string.IsNullOrWhiteSpace(field)) continue;
                if (string.Equals(field, token, StringComparison.OrdinalIgnoreCase)) tokenScore = Math.Max(tokenScore, 100);
                else if (field.StartsWith(token, StringComparison.OrdinalIgnoreCase)) tokenScore = Math.Max(tokenScore, 70);
                else if (field.Contains(token, StringComparison.OrdinalIgnoreCase)) tokenScore = Math.Max(tokenScore, 35);
            }
            if (tokenScore == 0) return 0;
            score += tokenScore;
        }
        return score;
    }
}
