namespace WinCare.App.ViewModels.Pages;

public sealed class HomePageViewModel : TabbedPageViewModel
{
    public HomePageViewModel() : base([
        new PageSection("Overview", "No status is available yet.", [
            new PageRow("System check", "Collect current Windows, storage, security, and restart evidence.", "Not run", "Read-only"),
            new PageRow("Safety checks", "Every change is previewed, approved, recorded, and recoverable.", "Protected", "Always enforced"),
            new PageRow("Change history", "Receipts and undo availability appear after a reviewed operation.", "No activity", "Nothing changed")]),
        new PageSection("Recommendations", "Recommendations appear only after WinCare has current evidence.", []),
        new PageSection("Favorites", "Pin frequently used checks and tools from All tools.", [])]) { }
}
