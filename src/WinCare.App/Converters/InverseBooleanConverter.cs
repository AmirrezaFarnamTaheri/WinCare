using System;
using Microsoft.UI.Xaml.Data;

namespace WinCare.App.Converters;

/// <summary>
/// Inverts a boolean for x:Bind converter scenarios such as disabling controls while work is running.
/// Null values fall back to true (enabled) so bindings without a FallbackValue keep controls usable.
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return !(value is bool enabled && enabled);
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return !(value is bool enabled && enabled);
    }
}
