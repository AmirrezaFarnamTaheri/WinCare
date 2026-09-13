namespace WinCare.App.ViewModels.Pages;

public sealed class SecurityPageViewModel : TabbedPageViewModel
{
    public string ToolSearchQuery => SelectedIndex switch
    {
        0 => "security",
        // Protection controls are cataloged as security-control commands.
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
