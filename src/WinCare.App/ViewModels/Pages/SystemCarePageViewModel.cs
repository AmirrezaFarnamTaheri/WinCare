using WinCare.Application.Tools;

namespace WinCare.App.ViewModels.Pages;

public sealed class SystemCarePageViewModel : TabbedPageViewModel
{
    public CareAreaSelection ToolSelection => SelectedIndex switch
    {
        0 => new("System care", "Clean up"),
        1 => new("System care", "Performance"),
        2 => new("System care", "Apps & startup"),
        3 => new("System care", "Network & updates"),
        _ => new("System care", ["Routines", "Maintenance"]),
    };

    public SystemCarePageViewModel() : base([
        new PageSection("Clean up", "No cleanup tools are available in this section.", []),
        new PageSection("Performance", "No performance tools are available in this section.", []),
        new PageSection("Apps & startup", "No app or startup tools are available in this section.", []),
        new PageSection("Network & updates", "No network or update tools are available in this section.", []),
        new PageSection("Routines & maintenance", "No routines are available in this section.", [])]) { }
}
