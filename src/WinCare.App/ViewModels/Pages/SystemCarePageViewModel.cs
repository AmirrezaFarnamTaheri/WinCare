namespace WinCare.App.ViewModels.Pages;

public sealed class SystemCarePageViewModel : TabbedPageViewModel
{
    public string ToolSearchQuery => SelectedIndex switch
    {
        0 => "storage cleanup installer",
        1 => "performance",
        2 => "startup applications residual winget",
        3 => "network update winget",
        _ => "preset",
    };

    public SystemCarePageViewModel() : base([
        new PageSection("Clean up", "No tools are available in this section.", []),
        new PageSection("Performance", "No tools are available in this section.", []),
        new PageSection("Apps & startup", "No tools are available in this section.", []),
        new PageSection("Network & updates", "No tools are available in this section.", []),
        new PageSection("Routines", "No tools are available in this section.", [])]) { }
}
