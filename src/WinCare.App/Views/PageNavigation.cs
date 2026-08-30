using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinCare.App.Views;

public static class PageNavigation
{
    public static void NavigateTo(DependencyObject source, string key)
    {
        ShellPage shell = FindShell(source);
        shell.NavigateTo(key);
    }

    public static void OpenTools(DependencyObject source, string query)
    {
        ShellPage shell = FindShell(source);
        shell.OpenGlobalSearch(query);
    }

    private static ShellPage FindShell(DependencyObject source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is ShellPage shell) return shell;
            current = VisualTreeHelper.GetParent(current);
        }

        throw new InvalidOperationException("The page is not attached to the WinCare shell.");
    }
}
