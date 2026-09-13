using Microsoft.Win32;

namespace WinCare.Infrastructure.Commands;

internal sealed partial class WindowsCommandExecutor
{
    internal static class EnvironmentSanitizerHelper
    {
        public sealed record PathAuditReport(
            int TotalEntries,
            int ValidEntries,
            int DeadEntries,
            int DuplicateEntries,
            int UserPathLength,
            int SystemPathLength,
            bool ExceedsRecommendedLength,
            IReadOnlyList<string> DeadPaths,
            IReadOnlyList<string> DuplicatePaths,
            string Summary);

        public sealed record PackageManagerCacheInfo(
            string ManagerName,
            string Path,
            bool Exists,
            long FileCount,
            ulong TotalBytes,
            string FormattedSize);

        public static PathAuditReport AuditPathEnvironment(bool includeSystem = true, bool includeUser = true)
        {
            var rawEntries = new List<string>();
            int userLen = 0;
            int sysLen = 0;

            if (includeUser)
            {
                using var userKey = Registry.CurrentUser.OpenSubKey("Environment");
                string? userPath = userKey?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                if (!string.IsNullOrWhiteSpace(userPath))
                {
                    userLen = userPath.Length;
                    rawEntries.AddRange(userPath.Split(';', StringSplitOptions.RemoveEmptyEntries));
                }
            }

            if (includeSystem)
            {
                using var sysKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment");
                string? sysPath = sysKey?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                if (!string.IsNullOrWhiteSpace(sysPath))
                {
                    sysLen = sysPath.Length;
                    rawEntries.AddRange(sysPath.Split(';', StringSplitOptions.RemoveEmptyEntries));
                }
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var duplicates = new List<string>();
            var dead = new List<string>();
            int validCount = 0;

            foreach (var raw in rawEntries)
            {
                string trimmed = raw.Trim().TrimEnd('\\');
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                if (!seen.Add(trimmed))
                {
                    duplicates.Add(trimmed);
                }

                string expanded = Environment.ExpandEnvironmentVariables(trimmed);
                if (Directory.Exists(expanded))
                {
                    validCount++;
                }
                else
                {
                    dead.Add(trimmed);
                }
            }

            bool exceedsLength = userLen > 2048 || sysLen > 8192;
            string summary = $"{rawEntries.Count} PATH entries audited · {validCount} valid · {dead.Count} dead paths · {duplicates.Count} duplicates";

            return new PathAuditReport(
                TotalEntries: rawEntries.Count,
                ValidEntries: validCount,
                DeadEntries: dead.Count,
                DuplicateEntries: duplicates.Count,
                UserPathLength: userLen,
                SystemPathLength: sysLen,
                ExceedsRecommendedLength: exceedsLength,
                DeadPaths: dead.AsReadOnly(),
                DuplicatePaths: duplicates.AsReadOnly(),
                Summary: summary);
        }

        public static IReadOnlyList<PackageManagerCacheInfo> ScanPackageManagerCaches()
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            var targets = new (string Name, string Path)[]
            {
                ("WinGet Packages", Path.Combine(localAppData, @"Microsoft\WinGet\Packages")),
                ("Scoop Cache", Path.Combine(userProfile, @"scoop\cache")),
                ("Chocolatey Cache", Path.Combine(programData, @"chocolatey\cache")),
                ("Electric Cache", Path.Combine(userProfile, @".electric\cache")),
                ("Cargo Registry Cache", Path.Combine(userProfile, @".cargo\registry\cache")),
                ("npm Cache", Path.Combine(appData, @"npm-cache"))
            };

            var results = new List<PackageManagerCacheInfo>();

            foreach (var (name, path) in targets)
            {
                if (!Directory.Exists(path))
                {
                    results.Add(new PackageManagerCacheInfo(name, path, false, 0, 0, "0 B"));
                    continue;
                }

                long fileCount = 0;
                ulong bytes = 0;

                try
                {
                    var dirInfo = new DirectoryInfo(path);
                    foreach (var file in dirInfo.EnumerateFiles("*", new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = true
                    }))
                    {
                        fileCount++;
                        bytes += (ulong)file.Length;
                    }
                }
                catch
                {
                    // Fail-safe graceful degradation
                }

                results.Add(new PackageManagerCacheInfo(
                    ManagerName: name,
                    Path: path,
                    Exists: true,
                    FileCount: fileCount,
                    TotalBytes: bytes,
                    FormattedSize: FormatBytes(bytes)));
            }

            return results.AsReadOnly();
        }

        private static string FormatBytes(ulong bytes)
        {
            if (bytes >= 1024UL * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
            if (bytes >= 1024UL * 1024) return $"{bytes / (1024.0 * 1024):F2} MB";
            if (bytes >= 1024UL) return $"{bytes / 1024.0:F2} KB";
            return $"{bytes} B";
        }
    }
}
