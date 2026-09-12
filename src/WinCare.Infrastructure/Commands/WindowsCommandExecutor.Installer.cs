using Microsoft.Win32;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;

namespace WinCare.Infrastructure.Commands;

/// <summary>
/// Read-only Windows Installer cache inspection. This collector deliberately reports
/// registration evidence only: absence of an observed registration is never a deletion
/// recommendation.
/// </summary>
internal sealed partial class WindowsCommandExecutor
{
    private const int DefaultInstallerCacheFileLimit = 5_000;
    private const int MaximumInstallerCacheFileLimit = 20_000;
    private const int MaximumInstallerRegistrationRecords = 20_000;

    // Test seams keep unit tests off the real Installer cache and registry. Production
    // always uses the bounded filesystem and registry collectors below.
    internal Func<string, int, CancellationToken, InstallerCacheEnumeration>? InstallerCacheEnumerationSeam { get; set; }
    internal Func<CancellationToken, InstallerRegistrationEvidence>? InstallerRegistrationEvidenceSeam { get; set; }

    internal sealed record InstallerCacheFile(string Path, long Size, DateTimeOffset LastWriteTimeUtc);
    internal sealed record InstallerCacheEnumeration(
        IReadOnlyList<InstallerCacheFile> Files,
        bool Truncated,
        IReadOnlyList<string> Errors);
    internal sealed record InstallerRegistration(string Path, string Kind, string RegistryPath);
    internal sealed record InstallerRegistrationEvidence(
        IReadOnlyList<InstallerRegistration> Registrations,
        bool Truncated,
        IReadOnlyList<string> Errors);

    private Task<CommandHandlerOutcome> InstallerCacheAnalysisAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        int maximumFiles = p.Int32("MaxFiles", DefaultInstallerCacheFileLimit, 1, MaximumInstallerCacheFileLimit);
        string cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Installer");
        InstallerCacheEnumeration cache = InstallerCacheEnumerationSeam?.Invoke(cacheRoot, maximumFiles, cancellationToken)
            ?? EnumerateInstallerCache(cacheRoot, maximumFiles, cancellationToken);
        InstallerRegistrationEvidence registrations = InstallerRegistrationEvidenceSeam?.Invoke(cancellationToken)
            ?? ReadInstallerRegistrationEvidence(cancellationToken);

        var ownersByPath = registrations.Registrations
            .GroupBy(record => NormalizeInstallerPath(record.Path), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key is not null)
            .ToDictionary(
                group => group.Key!,
                group => (IReadOnlyList<object>)group.Select(record => (object)new { kind = record.Kind, registryPath = record.RegistryPath }).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var files = cache.Files.Select(file =>
        {
            string? normalizedPath = NormalizeInstallerPath(file.Path);
            IReadOnlyList<object> owners = Array.Empty<object>();
            bool registered = false;
            if (normalizedPath is not null &&
                ownersByPath.TryGetValue(normalizedPath, out IReadOnlyList<object>? foundOwners) &&
                foundOwners is not null)
            {
                owners = foundOwners;
                registered = true;
            }
            return new
            {
                path = file.Path,
                size = file.Size,
                lastWriteTimeUtc = file.LastWriteTimeUtc,
                extension = Path.GetExtension(file.Path).ToLowerInvariant(),
                registrationEvidence = owners,
                registrationState = registered ? "registered" : "unmatched",
                disposition = "retained",
                retentionReason = registered
                    ? "Windows Installer product or patch registration references this cache file."
                    : "No matching registration was observed. This heuristic result is not a safety decision and the file is retained."
            };
        }).ToArray();

        bool incomplete = cache.Truncated || registrations.Truncated || cache.Errors.Count != 0 || registrations.Errors.Count != 0;
        return Task.FromResult(Success("installer-cache-analysis", incomplete
            ? "Installer cache analysis completed with incomplete evidence; every cache file remains retained."
            : "Installer cache analysis completed; every cache file remains retained.", new
        {
            cacheRoot,
            maximumFiles,
            files,
            registeredProductOrPatchRecords = registrations.Registrations.Count,
            scanComplete = !incomplete,
            cacheEnumerationTruncated = cache.Truncated,
            registrationEnumerationTruncated = registrations.Truncated,
            cacheEnumerationErrors = cache.Errors,
            registrationEnumerationErrors = registrations.Errors,
            safety = "Read-only evidence only. Unmatched files are never declared safe to delete, moved, or removed.",
        }));
    }

    private static InstallerCacheEnumeration EnumerateInstallerCache(string cacheRoot, int maximumFiles, CancellationToken cancellationToken)
    {
        var files = new List<InstallerCacheFile>(Math.Min(maximumFiles, 1024));
        var errors = new List<string>();
        bool truncated = false;
        try
        {
            foreach (string path in Directory.EnumerateFiles(cacheRoot, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string extension = Path.GetExtension(path);
                if (!extension.Equals(".msi", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".msp", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (files.Count == maximumFiles)
                {
                    truncated = true;
                    break;
                }
                try
                {
                    var info = new FileInfo(path);
                    files.Add(new InstallerCacheFile(info.FullName, info.Length, info.LastWriteTimeUtc));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                {
                    errors.Add($"Could not read cache file '{path}': {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            errors.Add($"Could not enumerate Windows Installer cache '{cacheRoot}': {ex.Message}");
        }
        return new InstallerCacheEnumeration(files, truncated, errors);
    }

    private static InstallerRegistrationEvidence ReadInstallerRegistrationEvidence(CancellationToken cancellationToken)
    {
        var registrations = new List<InstallerRegistration>();
        var errors = new List<string>();
        bool truncated = false;
        int registrationKeysVisited = 0;
        foreach (RegistryView view in RegistryViews())
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using RegistryKey? userData = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData", writable: false);
                if (userData is null)
                    continue;
                foreach (string sid in userData.GetSubKeyNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using RegistryKey? products = userData.OpenSubKey($"{sid}\\Products", writable: false);
                    if (products is not null)
                    {
                        foreach (string product in products.GetSubKeyNames())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (++registrationKeysVisited > MaximumInstallerRegistrationRecords) { truncated = true; break; }
                            using RegistryKey? productKey = products.OpenSubKey(product, writable: false);
                            if (productKey is null) continue;
                            AddLocalPackage(productKey.OpenSubKey("InstallProperties", writable: false), "product", $"HKLM[{view}]\\...\\UserData\\{sid}\\Products\\{product}\\InstallProperties", registrations, errors);
                        }
                    }
                    if (truncated) break;

                    // Windows Installer stores patch LocalPackage values in the SID-level
                    // Patches tree. Product-level Patches names are references, not the
                    // authoritative cache-path values, so they are intentionally not used
                    // as ownership evidence here.
                    using RegistryKey? patches = userData.OpenSubKey($"{sid}\\Patches", writable: false);
                    if (patches is null) continue;
                    foreach (string patch in patches.GetSubKeyNames())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (++registrationKeysVisited > MaximumInstallerRegistrationRecords) { truncated = true; break; }
                        using RegistryKey? patchKey = patches.OpenSubKey(patch, writable: false);
                        if (patchKey is not null)
                            AddLocalPackage(patchKey, "patch", $"HKLM[{view}]\\...\\UserData\\{sid}\\Patches\\{patch}", registrations, errors);
                    }
                    if (truncated) break;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                errors.Add($"Could not read Windows Installer registration in {view}: {ex.Message}");
            }
            if (truncated) break;
        }
        return new InstallerRegistrationEvidence(registrations, truncated, errors);
    }

    private static void AddLocalPackage(RegistryKey? key, string kind, string registryPath, List<InstallerRegistration> registrations, List<string> errors)
    {
        using (key)
        {
            if (key is null) return;
            try
            {
                if (key.GetValue("LocalPackage", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string path && !string.IsNullOrWhiteSpace(path))
                    registrations.Add(new InstallerRegistration(path, kind, registryPath));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                errors.Add($"Could not read LocalPackage at '{registryPath}': {ex.Message}");
            }
        }
    }

    private static IEnumerable<RegistryView> RegistryViews() => Environment.Is64BitOperatingSystem
        ? [RegistryView.Registry64, RegistryView.Registry32]
        : [RegistryView.Default];

    private static string? NormalizeInstallerPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
}
