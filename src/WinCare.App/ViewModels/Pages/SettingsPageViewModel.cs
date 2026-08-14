namespace WinCare.App.ViewModels.Pages;

public sealed class SettingsPageViewModel : TabbedPageViewModel
{
    public SettingsPageViewModel() : base([
        new PageSection("General", "No general settings are available.", [
            new PageRow("Startup behavior", "Open Home and restore the last safe navigation state.", "Default", "No background checks at startup")]),
        new PageSection("Appearance", "Appearance follows Windows by default.", [
            new PageRow("App theme", "Use the Windows light, dark, or High Contrast preference.", "System", "Segoe UI Variable")]),
        new PageSection("Safety", "Safety settings are enforced by policy.", [
            new PageRow("Review before changes", "Require a plan and explicit approval for every mutation.", "On", "Cannot be bypassed"),
            new PageRow("Recovery records", "Keep receipts and compensation data for supported changes.", "On", "Local application storage")]),
        new PageSection("Notifications", "No notification preferences have been changed.", [
            new PageRow("Completion notifications", "Notify only when a long-running operation finishes or needs attention.", "On", "No promotional notifications")]),
        new PageSection("Data", "No cached check data is available.", [
            new PageRow("Cached status", "Last-known results are labeled with their collection time and can be cleared.", "Empty", "Local only")]),
        new PageSection("Advanced", "Advanced options are unchanged.", [
            new PageRow("Technical details", "Show command IDs, source records, native status codes, and diagnostic evidence.", "Off", "Available per result")])]) { }
}
