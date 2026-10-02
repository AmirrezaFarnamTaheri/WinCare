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
        4 => new("System care", ["Routines", "Maintenance"]),
        5 => new("System care", "Developer"),
        6 => new("System care", "Desktop"),
        7 => new("System care", "Servicing"),
        8 => new("System care", "Virtualization"),
        9 => new("System care", "Guard"),
        10 => new("System care", "Storage"),
        _ => new("System care", "Clean up")
    };

    public bool IsPerformanceSection => SelectedIndex == 1;

    public override void SelectSection(int index)
    {
        base.SelectSection(index);
        OnPropertyChanged(nameof(IsPerformanceSection));
    }

    public SystemCarePageViewModel() : base([
                new PageSection("Clean up", "No cleanup tools are available in this section.", []),
        new PageSection("Performance", "No performance tools are available in this section.", []),
        new PageSection("Apps & startup", "No app or startup tools are available in this section.", []),
        new PageSection("Network & updates", "No network or update tools are available in this section.", []),
        new PageSection("Routines & maintenance", "No routines are available in this section.", []),
        new PageSection("Developer", "No developer tools are available in this section.", []),
        new PageSection("Desktop", "No desktop tools are available in this section.", []),
        new PageSection("Servicing", "No servicing tools are available in this section.", []),
        new PageSection("Virtualization", "No virtualization tools are available in this section.", []),
        new PageSection("Guard", "No guard tools are available in this section.", []),
        new PageSection("Storage", "No storage tools are available in this section.", [])]) { }
}
