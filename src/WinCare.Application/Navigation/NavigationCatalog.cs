namespace WinCare.Application.Navigation;

/// <summary>
/// Frozen navigation topology for the native shell.
/// </summary>
public static class NavigationCatalog
{
    /// <summary>
    /// Ordered navigation items, including footer items.
    /// </summary>
    // Source-Driven Development Citation:
    // Pattern: C# 12 collection expressions targeting IReadOnlyList<T>
    // Source: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/collection-expressions
    // "Collection expressions can be assigned to read-only interface types, producing an immutable list structure."
    public static IReadOnlyList<NavigationDefinition> Items { get; } =
    [
        new("home", "Home", "NavHome", ["Overview", "Recommendations", "Favorites"]),
        new("checkup", "Checkup", "NavCheckup", ["Quick check", "Full check", "Custom check", "Results"]),
        new("system-care", "System care", "NavSystemCare", ["Clean up", "Performance", "Apps & startup", "Network & updates", "Routines"]),
        new("security", "Security", "NavSecurity", ["Status", "Protection", "Privacy", "Hardening"]),
        new("repair-recovery", "Repair & recovery", "NavRepairRecovery", ["Repair", "Restore", "Undo", "Backup", "Reset & media"]),
        new("all-tools", "All tools", "NavAllTools", ["Commands", "Categories", "Favorites", "Recent", "Presets"]),
        new("activity", "Activity", "NavActivity", ["Running", "Needs attention", "Completed", "Reports"]),
        new("settings", "Settings", "NavSettings", ["General", "Appearance", "Safety", "Notifications", "Data", "Advanced"], IsFooter: true),
    ];
}
