namespace WinCare.App.ViewModels.Pages;

public sealed class SecurityPageViewModel : TabbedPageViewModel
{
    public SecurityPageViewModel() : base([
        new PageSection("Status", "No security status has been collected.", [
            new PageRow("Windows Security", "Protection state, recent threats, and update freshness.", "Not checked", "Read-only"),
            new PageRow("Firewall", "Profile state, rule posture, and unexpected exposure.", "Not checked", "Read-only")]),
        new PageSection("Protection", "Protection findings will appear after a check.", [
            new PageRow("Defender and firewall", "Review protection settings before applying any change.", "Available", "Administrator approval may be required")]),
        new PageSection("Privacy", "No privacy assessment is available.", [
            new PageRow("Permissions and telemetry", "Review privacy-related Windows settings with plain-language effects.", "Available", "Review first")]),
        new PageSection("Hardening", "No hardening plan has been created.", [
            new PageRow("Advanced security configuration", "WDAC, VBS, HVCI, DMA, account, and policy tools.", "Available", "High-impact review")])]) { }
}
