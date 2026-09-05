using System.Reflection;
using Microsoft.UI.Xaml.Controls;

namespace WinCare.App.Views.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        VersionText = ResolveVersionText();
        InitializeComponent();
    }

    public string VersionText { get; }

    private static string ResolveVersionText()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(AboutPage).Assembly;
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            int buildMetadata = informational.IndexOf('+');
            return buildMetadata >= 0 ? informational[..buildMetadata] : informational;
        }

        Version? version = assembly.GetName().Version;
        return version is null ? "Version unavailable" : version.ToString();
    }
}
