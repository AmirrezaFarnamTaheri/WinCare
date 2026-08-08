using Microsoft.UI.Xaml;

namespace WinCare.App.Views;

public static class LayoutVisibility
{
    public static Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility InvertBoolToVisibility(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
}
