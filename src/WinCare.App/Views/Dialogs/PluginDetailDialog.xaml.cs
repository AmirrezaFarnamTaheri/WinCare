using Microsoft.UI.Xaml.Controls;
using WinCare.Application.Plugins;

namespace WinCare.App.Views.Dialogs;

public sealed partial class PluginDetailDialog : ContentDialog
{
    public RemotePluginItem PluginItem { get; }

    /// <summary>Initializes a new instance of <see cref="PluginDetailDialog"/>.</summary>
    public PluginDetailDialog(RemotePluginItem item, bool allowInstall = false)
    {
        InitializeComponent();
        PluginItem = item;

        PluginNameText.Text = item.Name;
        PluginAuthorText.Text = $"by {item.Author}";
        PluginVersionText.Text = $"v{item.Version}";
        PluginDescriptionText.Text = item.Description;
        PluginCategoryText.Text = item.Category;
        PluginPublishedText.Text = item.PublishedDate.ToString("yyyy-MM-dd");

        PermissionsItemsControl.ItemsSource = item.Permissions.Count > 0
            ? item.Permissions
            : new[] { "Standard access" };

        bool hasPublisherSignature = !string.IsNullOrWhiteSpace(item.PublicKeyPem) &&
                                     !string.IsNullOrWhiteSpace(item.Signature);
        bool verified = item.IsCatalogTrustVerified && hasPublisherSignature &&
                        !string.IsNullOrWhiteSpace(item.PackageUrl);

        PublisherTrustText.Text = item.IsRevoked
            ? "Revoked"
            : verified ? "Verified package"
            : hasPublisherSignature ? "Signed package"
            : "Community";
        bool canInstall = allowInstall && verified && !item.IsRevoked;
        IsPrimaryButtonEnabled = canInstall;
        PrimaryButtonText = allowInstall
            ? (canInstall ? "Install" : "Can't install")
            : string.Empty;
        CloseButtonText = allowInstall ? "Cancel" : "Close";

        if (item.IsRevoked)
        {
            RevocationBanner.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            RevocationReasonText.Text = item.RevocationReason ?? "This extension was blocked for security reasons.";
            IsPrimaryButtonEnabled = false;
            if (allowInstall) PrimaryButtonText = "Can't install";
        }

        CommandsProvidedText.Text = item.CommandsProvided.Count > 0
            ? string.Join(", ", item.CommandsProvided)
            : "None";
    }
}
