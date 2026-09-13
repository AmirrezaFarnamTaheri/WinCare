namespace WinCare.Application.Navigation;

/// <summary>
/// Product navigation topology. Primary items represent user jobs; secondary routes remain
/// available without competing for permanent rail space.
/// </summary>
public static class NavigationCatalog
{
    public static IReadOnlyList<NavigationDefinition> Items { get; } =
    [
        new("home", "Home", "NavHome", ["Overview", "Recommendations", "Recent activity"]),
        new("checkup", "Checkup", "NavCheckup", ["Quick check", "Results"]),
        new("system-care", "System care", "NavSystemCare", ["Clean up", "Performance", "Apps & startup", "Network & updates", "Routines & maintenance"]),
        new("security", "Security", "NavSecurity", ["Status", "Protection", "Privacy", "Hardening"]),
        new("repair-recovery", "Repair & recovery", "NavRepairRecovery", ["Repair", "Restore", "Backup", "Reset & media", "Portable playbooks"]),
        new("all-tools", "Power tools", "NavAllTools", ["Tools", "Categories", "Favorites", "Recent", "Care plans"]),
        new("activity", "Activity", "NavActivity", ["Running", "Needs attention", "Completed", "Reports"]),
        new("plugin-store", "Extensions", "NavPluginStore", ["Installed", "Built in", "Categories"], IsFooter: true),
        new("ai-doctor", "Troubleshoot", "NavAiDoctor", ["Describe a problem", "Suggested steps"], IsFooter: true),
        new("settings", "Settings", "NavSettings", ["General", "Appearance", "Safety", "Notifications", "Data", "Advanced"], IsFooter: true),
        new("help", "Help", "NavHelp", ["Getting started", "Finding tools", "Safety", "Keyboard"], IsFooter: true),
        new("about", "About WinCare", "NavAbout", ["Version", "Licenses"], IsHidden: true),
    ];
}
