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

    private void CopyBtc_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        CopyToClipboard("bc1q68g4m4denjw4smhvwmnz5fychuj3ge2vupx07w");
    }

    private void CopyEth_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        CopyToClipboard("0xbd5af5d1517317111db9523d6bb42fceae887abb");
    }

    private void CopyTron_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        CopyToClipboard("TRjFLA1Dd32Bw1i3FxjZW5dmVub5UfXFSS");
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AboutPage] Failed to copy to clipboard: {ex.Message}");
        }
    }
}
