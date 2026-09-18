using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using WinCare.App.Services;
using Windows.UI.ViewManagement;

namespace WinCare.App.Converters;

/// <summary>
/// Resolves a ViewModel-provided theme resource key into a brush at the view boundary.
/// Keeps Microsoft.UI.Xaml types out of ViewModels while retaining dynamic theme resources.
/// </summary>
public sealed class ThemeResourceBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, SolidColorBrush> LiveBrushes = new(StringComparer.Ordinal);
    // ResolveColor runs per binding evaluation (Checkup hits it ~15 times per refresh), so the
    // theme lookup is memoized per (theme, key): the theme string is recomputed every call, so a
    // theme switch keys into a fresh entry instead of reading a stale color.
    private static readonly Dictionary<(string Theme, string Key), Windows.UI.Color> ResolvedColors = new();
    private static readonly AccessibilitySettings Accessibility = new();

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string resourceKey)
        {
            if (!LiveBrushes.TryGetValue(resourceKey, out SolidColorBrush? brush))
            {
                brush = new SolidColorBrush(Colors.Transparent);
                LiveBrushes.Add(resourceKey, brush);
            }
            brush.Color = ResolveColor(resourceKey);
            return brush;
        }

        return new SolidColorBrush(Colors.Transparent);
    }

    // A converted x:Bind value does not re-evaluate when RequestedTheme changes.
    // Keep stable brush instances and update their colors for cached pages too.
    public static void RefreshBrushes()
    {
        // High-contrast schemes may change without changing the theme name.
        ResolvedColors.Clear();
        foreach (var (key, brush) in LiveBrushes)
        {
            brush.Color = ResolveColor(key);
        }
    }

    private static Windows.UI.Color ResolveColor(string key)
    {
        MainWindow? window = (Microsoft.UI.Xaml.Application.Current as App)?.MainWindow;
        FrameworkElement? root = window is { IsClosed: false } ? window.Content as FrameworkElement : null;
        string theme = Accessibility.HighContrast ? "HighContrast"
            : root?.ActualTheme.ToString() ?? (AppPreferences.Theme == "System"
                ? Microsoft.UI.Xaml.Application.Current.RequestedTheme.ToString() : AppPreferences.Theme);
        if (ResolvedColors.TryGetValue((theme, key), out Windows.UI.Color cached)) return cached;

        Windows.UI.Color resolved = FindBrush(Microsoft.UI.Xaml.Application.Current.Resources, theme, key)?.Color ?? Colors.Transparent;
        ResolvedColors[(theme, key)] = resolved;
        return resolved;
    }

    private static SolidColorBrush? FindBrush(ResourceDictionary resources, string theme, string key)
    {
        if (resources.ThemeDictionaries.TryGetValue(theme, out object? themed) &&
            themed is ResourceDictionary dictionary && dictionary.TryGetValue(key, out object? value) &&
            value is SolidColorBrush themedBrush)
        {
            return themedBrush;
        }
        for (int i = resources.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            SolidColorBrush? merged = FindBrush(resources.MergedDictionaries[i], theme, key);
            if (merged is not null) return merged;
        }
        return resources.TryGetValue(key, out object? fallback) ? fallback as SolidColorBrush : null;
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("Theme resource brushes are one-way view values.");
}
