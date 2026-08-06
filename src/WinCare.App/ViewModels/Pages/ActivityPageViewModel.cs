namespace WinCare.App.ViewModels.Pages;

public sealed class ActivityPageViewModel : TabbedPageViewModel
{
    public ActivityPageViewModel() : base([
        new PageSection("Running", "No operations are running.", []),
        new PageSection("Needs attention", "No operations need attention.", []),
        new PageSection("Completed", "Completed native operations will appear here.", []),
        new PageSection("Reports", "No native reports are available.", [])]) { }
}
