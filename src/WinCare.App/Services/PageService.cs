using Microsoft.UI.Xaml.Controls;
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
        ["settings"] = typeof(SettingsPage),
        ["help"] = typeof(HelpPage),
        ["about"] = typeof(AboutPage),
    };

    public Type GetPageType(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Pages.TryGetValue(key, out Type? pageType)
            ? pageType
            : throw new KeyNotFoundException($"Unknown navigation key '{key}'.");
    }

    public bool Navigate(Frame frame, string key, object? parameter = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Type pageType = GetPageType(key);
        return frame.CurrentSourcePageType == pageType && parameter is null
            ? false
            : frame.Navigate(pageType, parameter);
    }
}
