namespace WinCare.Application.Tools;

/// <summary>
/// A deterministic care-area projection. Care pages use catalog taxonomy rather than
/// fuzzy search strings so a tool cannot leak into an unrelated product section.
/// </summary>
public sealed record CareAreaSelection(string Area, IReadOnlyList<string> Sections)
{
    public CareAreaSelection(string area, string section) : this(area, [section]) { }

    public bool Matches(string area, string section) =>
        string.Equals(Area, area, StringComparison.OrdinalIgnoreCase) &&
        Sections.Any(candidate => string.Equals(candidate, section, StringComparison.OrdinalIgnoreCase));
}
