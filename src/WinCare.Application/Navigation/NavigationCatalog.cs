namespace WinCare.Application.Navigation;

/// <summary>
/// Frozen navigation topology for the native shell.
/// </summary>
public static class NavigationCatalog
{
    /// <summary>
    /// Ordered navigation items, including footer items.
    /// </summary>
    public static IReadOnlyList<NavigationDefinition> Items { get; } = Array.AsReadOnly<NavigationDefinition>(
    [
        new("home", "Home", "NavHome", ["Overview", "Recommendations", "Favorites"]),
        new("checkup", "Checkup", "NavCheckup", ["Quick check", "Full check", "Custom check", "Results"]),
        new("system-care", "System care", "NavSystemCare", ["Clean up", "Performance", "Apps & startup", "Network & updates", "Routines"]),
        new("security", "Security", "NavSecurity", ["Status", "Protection", "Privacy", "Hardening"]),
        new("repair-recovery", "Repair & recovery", "NavRepairRecovery", ["Repair", "Restore", "Undo", "Backup", "Reset & media"]),
        new("all-tools", "All tools", "NavAllTools", ["Commands", "Categories", "Favorites", "Recent", "Presets"]),
        new("activity", "Activity", "NavActivity", ["Running", "Needs attention", "Completed", "Reports"]),
        new("settings", "Settings", "NavSettings", ["General", "Appearance", "Safety", "Notifications", "Data", "Advanced"], IsFooter: true),
    ]);
}
