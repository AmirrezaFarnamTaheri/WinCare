namespace WinCare.Application.Tools;

/// <summary>
/// A deterministic care-area projection. Care pages use catalog taxonomy rather than
/// fuzzy search strings so a tool cannot leak into an unrelated product section.
/// </summary>
public sealed record CareAreaSelection(string Area, IReadOnlyList<string> Sections)
{
    /// <summary>Initializes a new instance of <see cref="CareAreaSelection"/>.</summary>
    public CareAreaSelection(string area, string section) : this(area, [section]) { }

    /// <summary>Determines whether the supplied area and section match this selection.</summary>
    public bool Matches(string area, string section) =>
        string.Equals(Area, area, StringComparison.OrdinalIgnoreCase) &&
        Sections.Any(candidate => string.Equals(candidate, section, StringComparison.OrdinalIgnoreCase));
}
