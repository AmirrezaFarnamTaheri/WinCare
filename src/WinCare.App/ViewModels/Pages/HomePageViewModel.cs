namespace WinCare.App.ViewModels.Pages;

public sealed class HomePageViewModel : TabbedPageViewModel
{
    public HomePageViewModel() : base([
        new PageSection("Overview", "No status is available yet.", [
            new PageRow("System check", "Run a check to collect current Windows, storage, security, and restart evidence.", "Not run", "Read-only"),
            new PageRow("Safety checks", "All changes use review, approval, evidence, and recovery contracts.", "On", "Always enforced"),
            new PageRow("Change history", "Native operations will appear here with receipts and undo availability.", "Empty", "No changes recorded")]),
        new PageSection("Recommendations", "Recommendations appear only after WinCare has current evidence.", []),
        new PageSection("Favorites", "Pin frequently used checks and tools from All tools.", [])]) { }
}
