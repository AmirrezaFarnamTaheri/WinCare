namespace WinCare.App.ViewModels.Pages;

public sealed class CheckupPageViewModel : TabbedPageViewModel
{
    public CheckupPageViewModel() : base([
        new PageSection("Quick check", "No quick check results are available.", [
            new PageRow("Windows and hardware", "Build, uptime, memory, processor, and device basics.", "Ready", "Read-only"),
            new PageRow("Storage", "Free space, volume state, and pressure thresholds.", "Ready", "Read-only"),
            new PageRow("Security", "Windows Security, firewall, updates, and restart state.", "Ready", "Read-only"),
            new PageRow("Updates", "Windows and driver update readiness, including restart state.", "Ready", "Read-only")]),
        new PageSection("Full check", "A full check has not been run.", [
            new PageRow("Complete diagnostic set", "Runs every admitted read-only check with individual progress and deadlines.", "Ready", "Cancellable")]),
        new PageSection("Custom check", "Choose at least one area to build a custom check.", [
            new PageRow("Select areas", "Choose system, storage, security, applications, network, or advanced checks.", "Ready", "Read-only")]),
        new PageSection("Results", "Completed check results will be listed here.", [])]) { }
}
