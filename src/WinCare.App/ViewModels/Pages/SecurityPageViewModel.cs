namespace WinCare.App.ViewModels.Pages;

public sealed class SecurityPageViewModel : TabbedPageViewModel
{
    public string ToolSearchQuery => SelectedIndex switch
    {
        0 => "security",
        // F-009: the catalog exposes protection controls as security-control-* commands;
        // "defender firewall" matched no tools at all.
        1 => "security-control",
        2 => "privacy",
        _ => "hardening",
    };

    public SecurityPageViewModel() : base([
        new PageSection("Status", "No security status has been collected.", [
            new PageRow("Windows Security", "Protection state, recent threats, and update freshness.", "Read-only tool", "Read-only"),
            new PageRow("Firewall", "Profile state, rule posture, and unexpected exposure.", "Read-only tool", "Read-only")]),
        new PageSection("Protection", "Protection findings will appear after a check.", [
            new PageRow("Defender and firewall", "Review protection settings before applying any change.", "Tool", "Administrator approval may be required")]),
        new PageSection("Privacy", "No privacy assessment is available.", [
            new PageRow("Permissions and telemetry", "Review privacy-related Windows settings with plain-language effects.", "Tool", "Review first")]),
        new PageSection("Hardening", "No hardening plan has been created.", [
            new PageRow("Advanced security configuration", "WDAC, VBS, HVCI, DMA, account, and policy tools.", "Tool", "High-impact review")])]) { }
}
