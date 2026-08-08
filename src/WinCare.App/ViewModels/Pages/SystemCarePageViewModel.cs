namespace WinCare.App.ViewModels.Pages;

public sealed class SystemCarePageViewModel : TabbedPageViewModel
{
    public SystemCarePageViewModel() : base([
        new PageSection("Clean up", "No cleanup assessment is available.", [
            new PageRow("Storage cleanup", "Review temporary files, caches, downloads, and bounded cleanup targets.", "Available", "Preview first"),
            new PageRow("Duplicate analysis", "Find duplicate files by size and verified content hash.", "Available", "Read-only scan")]),
        new PageSection("Performance", "No performance findings are available.", [
            new PageRow("Responsiveness", "Inspect memory pressure, startup load, services, and process evidence.", "Available", "Check before changes")]),
        new PageSection("Apps & startup", "No application inventory is available.", [
            new PageRow("Installed applications", "Review desktop, Store, and package inventory.", "Available", "Structured inventory"),
            new PageRow("Startup applications", "Review entries that start with Windows and their source.", "Available", "Preview first")]),
        new PageSection("Network & updates", "No network or update assessment is available.", [
            new PageRow("Connectivity", "Inspect adapters, routes, DNS, and bounded endpoint checks.", "Available", "Network only on demand"),
            new PageRow("Windows Update", "Search, review, and install admitted Windows updates.", "Available", "Restart may be required")]),
        new PageSection("Routines", "Saved routines will appear here.", [])]) { }
}
