using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace WinCare.App.Converters;

/// <summary>
/// Resolves a ViewModel-provided theme resource key into a brush at the view boundary.
/// Keeps Microsoft.UI.Xaml types out of ViewModels while retaining dynamic theme resources.
/// </summary>
public sealed class ThemeResourceBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string resourceKey &&
            Application.Current?.Resources?.TryGetValue(resourceKey, out object? resource) == true &&
            resource is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("Theme resource brushes are one-way view values.");
}
