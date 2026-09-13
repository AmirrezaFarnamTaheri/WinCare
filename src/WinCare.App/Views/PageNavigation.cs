using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;

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

    public static void OpenTool(DependencyObject source, string commandId, JsonElement parameters)
    {
        ShellPage shell = FindShell(source);
        shell.OpenTool(new ToolNavigationRequest(commandId, parameters.Clone()));
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

public sealed record ToolNavigationRequest(string CommandId, JsonElement Parameters);
