namespace WinCare.App.ViewModels.Pages;

using System;
using System.Collections.Generic;
using WinCare.Application.Plugins;

public sealed class PluginCardViewModel
{
    public PluginCardViewModel(PluginRegistryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Id = entry.Id;
        Name = entry.Name;
        Version = entry.Version;
        Author = entry.Author;
        Description = entry.Description;
        Category = entry.Category;
        IsBuiltIn = entry.IsBuiltIn;
        IsInstalled = true;
        InstalledState = entry.State;
        CommandCount = entry.Commands.Count;
        ErrorMessage = entry.ErrorMessage;
        Permissions = new List<string> { "In-Process Execution", "System Access" };
        PackageUrl = string.Empty;
        Sha256 = string.Empty;
    }

    public PluginCardViewModel(RemotePluginItem item, bool isInstalled, PluginRegistryEntry? installedEntry = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        Id = item.Id;
        Name = item.Name;
        Version = item.Version;
        Author = item.Author;
        Description = item.Description;
        Category = item.Category;
        RemoteItem = item;
        IsInstalled = isInstalled;
        IsBuiltIn = installedEntry?.IsBuiltIn ?? false;
        InstalledState = installedEntry?.State;
        CommandCount = installedEntry?.Commands.Count ?? item.CommandsProvided.Count;
        ErrorMessage = installedEntry?.ErrorMessage;
        Permissions = item.Permissions != null ? new List<string>(item.Permissions) : new List<string>();
        PackageUrl = item.PackageUrl;
        Sha256 = item.Sha256;
    }

    public string Id { get; }
    public string Name { get; }
    public string Version { get; }
    public string Author { get; }
    public string Description { get; }
    public string Category { get; }
    public bool IsInstalled { get; }
    public bool IsBuiltIn { get; }
    public PluginState? InstalledState { get; }
    public RemotePluginItem? RemoteItem { get; }
    public int CommandCount { get; }
    public string? ErrorMessage { get; }
    public List<string> Permissions { get; }
    public string PackageUrl { get; }
    public string Sha256 { get; }

    public string StatusBadgeText => IsInstalled switch
    {
        true when IsBuiltIn => "BUILT-IN",
        true when InstalledState == PluginState.Enabled => "ENABLED",
        true when InstalledState == PluginState.Disabled => "DISABLED",
        true when InstalledState == PluginState.Error => "ERROR",
        _ => "ONLINE CATALOG"
    };

    public string PublisherTrustBadgeText => IsRevoked
        ? $"REVOKED: {RemoteItem?.RevocationReason ?? "Security Advisory"}"
        : IsVerifiedPublisher ? "TRUSTED CATALOG / SIGNED PACKAGE"
        : HasPublisherSignature ? "SIGNED PACKAGE / CATALOG UNVERIFIED"
        : "UNVERIFIED";

    public bool HasPublisherSignature =>
        !string.IsNullOrWhiteSpace(RemoteItem?.PublicKeyPem) &&
        !string.IsNullOrWhiteSpace(RemoteItem?.Signature);

    /// <summary>
    /// True only when publisher signing metadata arrived through an independently verified,
    /// pinned catalog boundary. Package-local or unanchored catalog keys never qualify.
    /// </summary>
    public bool IsVerifiedPublisher => RemoteItem?.IsCatalogTrustVerified == true && HasPublisherSignature;

    public bool IsRevoked => RemoteItem?.IsRevoked == true;

    public string InstallButtonText => IsRevoked ? "Revoked" : (IsInstalled ? "Installed" : "Install");
    public bool CanInstall =>
        !IsInstalled &&
        !string.IsNullOrWhiteSpace(PackageUrl) &&
        !string.IsNullOrWhiteSpace(Sha256) &&
        !IsRevoked &&
        IsVerifiedPublisher;

    public string InstallAvailabilityReason => IsRevoked
        ? RemoteItem?.RevocationReason ?? "This package has been revoked."
        : IsInstalled ? "This plugin is already installed."
        : string.IsNullOrWhiteSpace(PackageUrl) ? "This package has no download location."
        : string.IsNullOrWhiteSpace(Sha256) ? "This package has no integrity digest."
        : RemoteItem?.IsCatalogTrustVerified != true ? "Remote installation is disabled because the catalog signature is not anchored to a WinCare-pinned trust root."
        : !HasPublisherSignature ? "The trusted catalog entry has no publisher manifest signature."
        : "Ready to install.";
    public bool CanEnable => IsInstalled && !IsBuiltIn && InstalledState == PluginState.Disabled && !IsRevoked;
    public bool CanDisable => IsInstalled && !IsBuiltIn && InstalledState == PluginState.Enabled;
    public bool CanUninstall => IsInstalled && !IsBuiltIn;
}
