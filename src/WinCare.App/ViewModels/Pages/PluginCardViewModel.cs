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

    public string PublisherTrustBadgeText
    {
        get
        {
            if (RemoteItem?.IsRevoked == true)
            {
                return $"REVOKED: {RemoteItem.RevocationReason ?? "Security Advisory"}";
            }

            return WinCare.Infrastructure.Plugins.PluginInstallerService.VerifyPublisherAuthenticity(Author, RemoteItem?.Signature, out var level, RemoteItem?.PublicKeyPem) 
                ? level 
                : "Unverified Publisher";
        }
    }

    public bool IsVerifiedPublisher => RemoteItem?.IsRevoked != true && WinCare.Infrastructure.Plugins.PluginInstallerService.VerifyPublisherAuthenticity(Author, RemoteItem?.Signature, out _, RemoteItem?.PublicKeyPem);
    public bool IsRevoked => RemoteItem?.IsRevoked == true;

    public string InstallButtonText => IsRevoked ? "Revoked" : (IsInstalled ? "Installed" : "Install");
    // The current remote catalog is not independently signed or pinned. Do not turn
    // catalog-provided publisher data into a package-install trust root.
    public bool CanInstall => !IsInstalled && !string.IsNullOrEmpty(PackageUrl) && !IsRevoked && IsVerifiedPublisher;
    public bool CanEnable => IsInstalled && !IsBuiltIn && InstalledState == PluginState.Disabled && !IsRevoked;
    public bool CanDisable => IsInstalled && !IsBuiltIn && InstalledState == PluginState.Enabled;
    public bool CanUninstall => IsInstalled && !IsBuiltIn;
}
