using WinCare.Domain.Commands;

namespace WinCare.App.ViewModels.Pages;

public sealed record AreaFilterOption(string Label, string? Value);
public sealed record SectionFilterOption(string Label, string? Value);
public sealed record RiskFilterOption(string Label, RiskTier? Value);
