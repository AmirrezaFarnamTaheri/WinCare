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
                        string? installLocation = InferInstallLocation(
                            item?.GetValue("InstallLocation") as string,
                            item?.GetValue("DisplayIcon") as string,
                            item?.GetValue("UninstallString") as string);
                        var application = new InstalledApplication(registryPath, displayName.Trim(), item?.GetValue("Publisher") as string ?? string.Empty, installLocation, registryPath);
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

    internal static string? InferInstallLocation(string? installLocation, string? displayIcon, string? uninstallString)
    {
        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            string trimmed = installLocation.Trim().Trim('\"');
            if (!string.IsNullOrWhiteSpace(trimmed)) return trimmed;
        }

        if (!string.IsNullOrWhiteSpace(displayIcon))
        {
            string candidate = displayIcon.Trim().Trim('\"');
            int commaIdx = candidate.IndexOf(',');
            if (commaIdx > 2) candidate = candidate[..commaIdx].Trim().Trim('\"');
            try
            {
                string? dir = Path.GetDirectoryName(candidate);
                if (!string.IsNullOrWhiteSpace(dir)) return dir;
            }
            catch (ArgumentException) { }
        }

        if (!string.IsNullOrWhiteSpace(uninstallString))
        {
            string candidate = uninstallString.Trim();
            if (!candidate.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
            {
                if (candidate.StartsWith('\"'))
                {
                    int endQuote = candidate.IndexOf('\"', 1);
                    if (endQuote > 1) candidate = candidate[1..endQuote];
                }
                else
                {
                    int spaceIdx = candidate.IndexOf(' ');
                    if (spaceIdx > 0) candidate = candidate[..spaceIdx];
                }

                try
                {
                    string? dir = Path.GetDirectoryName(candidate);
                    if (!string.IsNullOrWhiteSpace(dir)) return dir;
                }
                catch (ArgumentException) { }
            }
        }

        return null;
    }
}
