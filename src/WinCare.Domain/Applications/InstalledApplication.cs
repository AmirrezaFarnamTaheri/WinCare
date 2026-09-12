namespace WinCare.Domain.Applications;

/// <summary>Read-only uninstall-registry evidence for an installed application.</summary>
public sealed record InstalledApplication(
    string Id,
    string DisplayName,
    string Publisher,
    string? InstallLocation,
    string RegistryPath);

/// <summary>Result of enumerating installed-application evidence.</summary>
public sealed record InstalledApplicationInventory(
    IReadOnlyList<InstalledApplication> Applications,
    bool IsPartial,
    IReadOnlyList<InstalledApplicationInventoryIssue> Issues);

/// <summary>A registry view that could not be read during inventory collection.</summary>
public sealed record InstalledApplicationInventoryIssue(string Source, string Message);
