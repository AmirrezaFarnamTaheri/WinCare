using System;
using Microsoft.UI.Xaml.Data;

namespace WinCare.App.Converters;

/// <summary>
/// Inverts a boolean for x:Bind converter scenarios such as disabling controls while work is running.
/// Null values fall back to true (enabled) so bindings without a FallbackValue keep controls usable.
/// </summary>
/// <remarks>
/// Every XAML use is <c>Mode=OneWay</c> with <c>FallbackValue=True</c>. ConvertBack inverts too, which
/// is the correct inverse for that direction, but it means a TwoWay binding would silently write the
/// inverted value back — keep uses one-way.
/// </remarks>
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
