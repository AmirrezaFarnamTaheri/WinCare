using Microsoft.Win32;
using WinCare.Application.Applications;
using WinCare.Domain.Applications;

namespace WinCare.Infrastructure.Applications;

/// <summary>Read-only uninstall-registry inventory for residual discovery.</summary>
public sealed class WindowsInstalledApplicationInventoryService : IInstalledApplicationInventoryService
{
    /// <inheritdoc />
    public Task<InstalledApplicationInventory> GetInstalledApplicationsAsync(CancellationToken cancellationToken) => Task.Run(() => Read(cancellationToken));

    private static InstalledApplicationInventory Read(CancellationToken cancellationToken)
    {
        var applications = new Dictionary<string, InstalledApplication>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<InstalledApplicationInventoryIssue>();
        foreach ((RegistryHive hive, RegistryView view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            if (cancellationToken.IsCancellationRequested) return Cancelled(applications, issues);
            string source = $"{hive}/{view}";
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                using RegistryKey? uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (string subkeyName in uninstall.GetSubKeyNames().Take(5_000))
                {
                    if (cancellationToken.IsCancellationRequested) return Cancelled(applications, issues);
                    try
                    {
                        using RegistryKey? item = uninstall.OpenSubKey(subkeyName);
                        string? displayName = item?.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(displayName)) continue;
                        string registryPath = $@"{source}\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{subkeyName}";
                        var application = new InstalledApplication(registryPath, displayName.Trim(), item?.GetValue("Publisher") as string ?? string.Empty, item?.GetValue("InstallLocation") as string, registryPath);
                        applications.TryAdd(application.Id, application);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    {
                        issues.Add(new InstalledApplicationInventoryIssue($"{source}/{subkeyName}", ex.Message));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                issues.Add(new InstalledApplicationInventoryIssue(source, ex.Message));
            }
        }
        return new InstalledApplicationInventory(applications.Values.OrderBy(application => application.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray(), issues.Count > 0, issues.ToArray());
    }

    private static InstalledApplicationInventory Cancelled(Dictionary<string, InstalledApplication> applications, List<InstalledApplicationInventoryIssue> issues)
    {
        issues.Add(new InstalledApplicationInventoryIssue("installed-applications", "Inventory was cancelled by the caller."));
        return new InstalledApplicationInventory(applications.Values.OrderBy(application => application.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray(), true, issues.ToArray());
    }
}
