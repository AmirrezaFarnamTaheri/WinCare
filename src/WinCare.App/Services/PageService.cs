using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using WinCare.Application.Navigation;
using WinCare.App.Views.Pages;

namespace WinCare.App.Services;

public sealed class PageService
{
    private static readonly IReadOnlyDictionary<string, Type> Pages = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        ["home"] = typeof(HomePage),
        ["checkup"] = typeof(CheckupPage),
        ["system-care"] = typeof(SystemCarePage),
        ["security"] = typeof(SecurityPage),
        ["repair-recovery"] = typeof(RepairRecoveryPage),
        ["all-tools"] = typeof(AllToolsPage),
        ["activity"] = typeof(ActivityPage),
        ["plugin-store"] = typeof(PluginStorePage),
        ["ai-doctor"] = typeof(AiDoctorPage),
        ["settings"] = typeof(SettingsPage),
        ["help"] = typeof(HelpPage),
        ["about"] = typeof(AboutPage),
    };

    static PageService()
    {
        // This id->type map and NavigationCatalog are two lists that must agree by hand: a key
        // present only here is a page the shell can never navigate to, and the XAML Tags are a
        // third. Assert at first use so the drift is caught in debug and smoke runs rather than
        // silently breaking a route.
        var catalogIds = new HashSet<string>(NavigationCatalog.Items.Select(item => item.Id), StringComparer.Ordinal);
        foreach (string key in Pages.Keys)
        {
            if (catalogIds.Contains(key)) continue;
            string message = $"PageService key '{key}' has no matching route in {nameof(NavigationCatalog)}.";
            Debug.WriteLine($"[PageService] {message}");
            Debug.Assert(false, message);
        }
    }

    public Type GetPageType(string key) => Pages.TryGetValue(key, out Type? pageType)
        ? pageType
        : throw new KeyNotFoundException($"Unknown navigation key '{key}'.");

    public string? GetNavigationKey(Type pageType) =>
        Pages.FirstOrDefault(pair => pair.Value == pageType).Key;

    public bool Navigate(Frame frame, string key, object? parameter = null)
    {
        Type pageType = GetPageType(key);
        return frame.CurrentSourcePageType == pageType && parameter is null
            ? false
            : frame.Navigate(pageType, parameter);
    }
}
