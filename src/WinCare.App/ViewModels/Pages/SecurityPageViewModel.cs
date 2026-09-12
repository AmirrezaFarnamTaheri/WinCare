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
        new PageSection("Status", "No tools are available in this section.", []),
        new PageSection("Protection", "No tools are available in this section.", []),
        new PageSection("Privacy", "No tools are available in this section.", []),
        new PageSection("Hardening", "No tools are available in this section.", [])]) { }
}
