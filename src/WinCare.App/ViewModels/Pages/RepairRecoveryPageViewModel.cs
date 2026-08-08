namespace WinCare.App.ViewModels.Pages;

public sealed class RepairRecoveryPageViewModel : TabbedPageViewModel
{
    public RepairRecoveryPageViewModel() : base([
        new PageSection("Repair", "No repair assessment is available.", [
            new PageRow("Windows components", "Diagnose component store, update, service, and package issues.", "Available", "Evidence required"),
            new PageRow("Network repair", "Build a bounded repair plan from current adapter and connectivity evidence.", "Available", "Review first")]),
        new PageSection("Restore", "No restore points are listed.", [
            new PageRow("System restore", "Inspect restore points and create or apply an admitted restore operation.", "Available", "Administrator approval required")]),
        new PageSection("Undo", "No reversible WinCare changes are available.", []),
        new PageSection("Backup", "No WinCare backup has been created.", [
            new PageRow("Export settings and reports", "Create a portable record before higher-impact work.", "Available", "Local files only")]),
        new PageSection("Reset & media", "No reset or recovery-media task is active.", [
            new PageRow("Recovery options", "Review reset, offline repair, and recovery media workflows.", "Available", "High-impact confirmation")])]) { }
}
