using System;

using Microsoft.UI.Xaml.Controls;

using WinCare.Application.Plugins;

namespace WinCare.App.Views.Dialogs;

public sealed partial class PluginDetailDialog : ContentDialog
{
    public RemotePluginItem PluginItem { get; }

    public PluginDetailDialog(RemotePluginItem item)
    {
        InitializeComponent();
        PluginItem = item ?? throw new ArgumentNullException(nameof(item));

        PluginNameText.Text = item.Name;
        PluginAuthorText.Text = $"by {item.Author}";
        PluginVersionText.Text = $"v{item.Version}";
        PluginDescriptionText.Text = item.Description;
        PluginCategoryText.Text = item.Category;
        PluginPublishedText.Text = item.PublishedDate.ToString("yyyy-MM-dd");

        PermissionsItemsControl.ItemsSource = item.Permissions.Count > 0 
            ? item.Permissions 
            : new[] { "Standard Execution (No special permissions)" };

        // Package signatures become publisher trust only when the catalog itself was verified
        // against a WinCare-pinned trust root. An unanchored catalog can still be browsed but
        // can never enable remote installation.
        bool hasPublisherSignature = !string.IsNullOrWhiteSpace(item.PublicKeyPem) &&
                                     !string.IsNullOrWhiteSpace(item.Signature);
        bool verified = item.IsCatalogTrustVerified && hasPublisherSignature &&
                        !string.IsNullOrWhiteSpace(item.PackageUrl);

        PublisherTrustText.Text = item.IsRevoked
            ? "Revoked"
            : verified ? "Trusted catalog / signed package"
            : hasPublisherSignature ? "Package signed; catalog trust root not verified"
            : "Publisher signature unavailable";
        IsPrimaryButtonEnabled = verified && !item.IsRevoked;
        PrimaryButtonText = verified && !item.IsRevoked ? "Trust and install" : "Installation unavailable";

        if (item.IsRevoked)
        {
            RevocationBanner.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            RevocationReasonText.Text = item.RevocationReason ?? "This package has been revoked by security policy.";
            IsPrimaryButtonEnabled = false;
            PrimaryButtonText = "Installation unavailable";
        }

        CommandsProvidedText.Text = item.CommandsProvided.Count > 0
            ? string.Join(", ", item.CommandsProvided)
            : "None";
    }
}
