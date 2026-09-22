namespace WinCare.Application.Navigation;

/// <summary>Navigation labels and search concepts used by the app shell.</summary>
public static class NavigationCatalog
{
    public static IReadOnlyList<NavigationDefinition> Items { get; } =
    [
        new("home", "Home", "NavHome", ["Overview", "Common care", "Recent activity"]),
        new("checkup", "Checkup", "NavCheckup", ["Checkup"]),
        new("system-care", "System care", "NavSystemCare", ["Clean up", "Performance", "Apps & startup", "Network & updates", "Routines & maintenance"]),
        new("security", "Security", "NavSecurity", ["Status", "Protection", "Privacy", "Hardening"]),
        new("repair-recovery", "Repair & recovery", "NavRepairRecovery", ["Repair", "Restore", "Backup", "Reset & media", "Portable playbooks"]),
        new("all-tools", "Power tools", "NavAllTools", ["Tools", "Categories", "Favorites", "Recent", "Care plans"]),
        new("activity", "Activity", "NavActivity", ["Running", "Needs attention", "History", "Reports"]),
        new("plugin-store", "Extensions", "NavPluginStore", ["Search extensions", "Categories", "Installed extensions", "Online catalog"], IsFooter: true),
        new("ai-doctor", "Troubleshoot", "NavAiDoctor", ["Describe a problem", "What WinCare found", "Suggested steps"], IsFooter: true),
        new("settings", "Settings", "NavSettings", ["App theme", "Remember window", "Local data"], IsFooter: true),
        new("help", "Help", "NavHelp", ["Getting started", "Find a tool", "Before changes", "Keyboard"], IsFooter: true),
        new("about", "About WinCare", "NavAbout", ["Version", "Open source", "Support", "Donate"], IsHidden: true),
    ];
}
