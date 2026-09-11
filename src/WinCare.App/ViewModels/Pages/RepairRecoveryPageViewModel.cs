namespace WinCare.App.ViewModels.Pages;

public sealed class RepairRecoveryPageViewModel : TabbedPageViewModel
{
    public string ToolSearchQuery => SelectedIndex switch
    {
        0 => "repair",
        1 => "restore",
        // F-009: the catalog has no undo command; the journal/receipt reports are where
        // reversible-change records live, so point the Undo tab's search there.
        2 => "reports",
        3 => "export backup",
        _ => "recovery reset",
    };

    public RepairRecoveryPageViewModel() : base([
        new PageSection("Repair", "No repair assessment is available.", [
            new PageRow("Windows components", "Diagnose component store, update, service, and package issues.", "Tool", "Evidence required"),
            new PageRow("Network repair", "Build a bounded repair plan from current adapter and connectivity evidence.", "Tool", "Review first")]),
        new PageSection("Restore", "No restore points are listed.", [
            new PageRow("System restore", "Inspect restore points and create or apply an admitted restore operation.", "Tool", "Administrator approval required")]),
        new PageSection("Undo", "No reversible WinCare changes are available.", []),
        new PageSection("Backup", "No WinCare backup has been created.", [
            new PageRow("Export settings and reports", "Create a portable record before higher-impact work.", "Tool", "Local files only")]),
        new PageSection("Reset & media", "No reset or recovery-media task is active.", [
            new PageRow("Recovery options", "Review reset, offline repair, and recovery media workflows.", "Tool", "High-impact confirmation")])]) { }
}
