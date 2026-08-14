namespace WinCare.App.ViewModels.Pages;

using System;
using System.Collections.Generic;
using WinCare.Application.Plugins;
using WinCare.Infrastructure.Plugins;

public sealed class PluginCardViewModel
{
    public PluginCardViewModel(PluginRegistryEntry entry)
    {
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
        Permissions = new List<string> { "Installed Local Plugin" };
        PackageUrl = string.Empty;
    }

    public PluginCardViewModel(RemotePluginItem item, bool isInstalled, PluginRegistryEntry? installedEntry = null)
    {
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
        Permissions = item.Permissions;
        PackageUrl = item.PackageUrl;
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

    public string StatusBadgeText => IsInstalled switch
    {
        true when IsBuiltIn => "BUILT-IN",
        true when InstalledState == PluginState.Enabled => "INSTALLED",
        true when InstalledState == PluginState.Disabled => "DISABLED",
        true when InstalledState == PluginState.Error => "ERROR",
        _ => "ONLINE CATALOG"
    };

    public string InstallButtonText => IsInstalled ? "Installed" : "Install";
    public bool CanInstall => !IsInstalled && !string.IsNullOrEmpty(PackageUrl);
}
