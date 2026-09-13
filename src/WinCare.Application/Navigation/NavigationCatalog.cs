namespace WinCare.Application.Navigation;

/// <summary>
/// Product navigation topology. Primary items represent user jobs; secondary routes remain
/// available without competing for permanent rail space. Search concepts mirror what each
/// live surface actually exposes so discovery cannot advertise phantom product areas.
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
        new("plugin-store", "Extensions", "NavPluginStore", ["Search extensions", "Categories", "Installed extensions", "Catalog trust"], IsFooter: true),
        new("ai-doctor", "Troubleshoot", "NavAiDoctor", ["Describe a problem", "Diagnostic findings", "Suggested steps"], IsFooter: true),
        new("settings", "Settings", "NavSettings", ["App theme", "Window continuity", "Local data", "Safety policy"], IsFooter: true),
        new("help", "Help", "NavHelp", ["Getting started", "Finding tools", "Safety", "Keyboard"], IsFooter: true),
        new("about", "About WinCare", "NavAbout", ["Version", "Licenses"], IsHidden: true),
    ];
}
