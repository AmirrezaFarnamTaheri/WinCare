using WinCare.Application.Tools;

namespace WinCare.App.ViewModels.Pages;

public sealed class SecurityPageViewModel : TabbedPageViewModel
{
    public CareAreaSelection ToolSelection => SelectedIndex switch
    {
        0 => new("Security", "Status"),
        1 => new("Security", "Protection"),
        2 => new("Security", "Privacy"),
        _ => new("Security", "Hardening"),
    };

    /// <summary>Initializes a new instance of <see cref="SecurityPageViewModel"/>.</summary>
    public SecurityPageViewModel() : base([
        new PageSection("Status", "No security status tools are available in this section.", []),
        new PageSection("Protection", "No protection tools are available in this section.", []),
        new PageSection("Privacy", "No privacy tools are available in this section.", []),
        new PageSection("Hardening", "No hardening tools are available in this section.", [])]) { }
}
