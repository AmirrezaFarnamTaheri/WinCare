namespace WinCare.App.ViewModels.Pages;

public sealed record PageSection(string Name, string EmptyMessage, IReadOnlyList<PageRow> Rows);
