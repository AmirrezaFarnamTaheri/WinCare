namespace WinCare.Application.Navigation;

/// <summary>Kind of entity a global-search suggestion points at.</summary>
public enum GlobalSearchSuggestionKind
{
    Page,
    Tool,
    Extension,
    Help,
}

/// <summary>One ranked global-search suggestion and the route it opens.</summary>
public sealed record GlobalSearchSuggestion(
    string Title,
    string Subtitle,
    string Route,
    string? Query,
    GlobalSearchSuggestionKind Kind)
{
    public string KindLabel => Kind switch
    {
        GlobalSearchSuggestionKind.Page => "Page",
        GlobalSearchSuggestionKind.Tool => "Tool",
        GlobalSearchSuggestionKind.Extension => "Extension",
        GlobalSearchSuggestionKind.Help => "Help",
        _ => string.Empty,
    };
}
