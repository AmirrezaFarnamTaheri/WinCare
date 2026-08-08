using WinCare.CommandCatalog.Models;

namespace WinCare.App.ViewModels.Pages;

public sealed record AreaFilterOption(string Label, string? Value);
public sealed record RiskFilterOption(string Label, CommandRisk? Value);
