namespace WinCare.Application.Navigation;

/// <summary>
/// Single navigation entry and its hosted tabs.
/// </summary>
/// <param name="Id">Stable navigation ID.</param>
/// <param name="Label">Display label.</param>
/// <param name="AutomationId">Automation identifier for UIA smoke tests.</param>
/// <param name="Tabs">Ordered tab titles.</param>
/// <param name="IsFooter">Whether the item belongs in the secondary/footer navigation.</param>
/// <param name="IsHidden">Whether the route is available through search/deep links but not pinned in the rail.</param>
public sealed record NavigationDefinition(
    string Id,
    string Label,
    string AutomationId,
    IReadOnlyList<string> Tabs,
    bool IsFooter = false,
    bool IsHidden = false);
