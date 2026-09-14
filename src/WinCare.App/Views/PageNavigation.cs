using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;

namespace WinCare.App.Views;

public static class PageNavigation
{
    /// <summary>Navigates to the requested product page.</summary>
    public static void NavigateTo(DependencyObject source, string key, object? parameter = null) =>
        FindShell(source).NavigateTo(key, parameter);

    /// <summary>Navigates to a named section of a product page.</summary>
    public static void NavigateToSection(DependencyObject source, string key, string sectionTitle) =>
        NavigateTo(source, key, new SectionNavigationRequest(sectionTitle));

    /// <summary>Resolves section index.</summary>
    public static int ResolveSectionIndex(SelectorBar selector, object? parameter)
    {
        if (parameter is not SectionNavigationRequest request) return -1;
        for (int index = 0; index < selector.Items.Count; index++)
        {
            if (selector.Items[index] is SelectorBarItem item &&
                string.Equals(item.Text, request.SectionTitle, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }
        return -1;
    }

    /// <summary>Opens tools.</summary>
    public static void OpenTools(DependencyObject source, string query) =>
        FindShell(source).OpenGlobalSearch(query);

    /// <summary>Opens tool.</summary>
    public static void OpenTool(DependencyObject source, string commandId, JsonElement parameters) =>
        FindShell(source).OpenTool(new ToolNavigationRequest(commandId, parameters.Clone()));

    /// <summary>Shows tour.</summary>
    public static Task ShowTourAsync(DependencyObject source) => FindShell(source).ShowTourAsync();

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
public sealed record SectionNavigationRequest(string SectionTitle);
