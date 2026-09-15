namespace WinCare.App.ViewModels;

public enum GlobalSearchSuggestionKind
{
    Page,
    Tool,
    Extension,
    Help,
}

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
