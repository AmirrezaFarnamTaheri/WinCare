using System.Security.Cryptography;

namespace WinCare.Infrastructure.Commands;

internal sealed partial class WindowsCommandExecutor
{
    internal static class StorageDeduplicationHelper
    {
        public sealed record DuplicateCandidateSet(
            string FullSha256,
            long FileSizeBytes,
            int DuplicateCount,
            ulong ReclaimableBytes,
            IReadOnlyList<string> FilePaths);

        public sealed record DuplicateScanReport(
            string ScannedDirectory,
            int FilesAudited,
            int DuplicateGroupsCount,
            int TotalDuplicateFiles,
            ulong PotentialReclaimableBytes,
            string FormattedReclaimableSize,
            IReadOnlyList<DuplicateCandidateSet> DuplicateSets,
            string Summary);

        public static DuplicateScanReport ScanDuplicateCandidates(string directoryPath, int maxFilesToCheck = 500)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return new DuplicateScanReport(
                    ScannedDirectory: directoryPath ?? string.Empty,
                    FilesAudited: 0,
                    DuplicateGroupsCount: 0,
                    TotalDuplicateFiles: 0,
                    PotentialReclaimableBytes: 0,
                    FormattedReclaimableSize: "0 B",
                    DuplicateSets: Array.Empty<DuplicateCandidateSet>(),
                    Summary: "Target directory does not exist.");
            }

            var candidateFiles = new List<FileInfo>();
            try
            {
                var dir = new DirectoryInfo(directoryPath);
                foreach (var file in dir.EnumerateFiles("*", new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true
                }))
                {
                    if (file.Length > 0)
                    {
                        candidateFiles.Add(file);
                        if (candidateFiles.Count >= maxFilesToCheck) break;
                    }
                }
            }
            catch
            {
                // Fail-safe graceful degradation
            }

            // Phase 1: Size grouping (files with unique sizes cannot be duplicates)
            var sizeGroups = candidateFiles
                .GroupBy(f => f.Length)
                .Where(g => g.Count() > 1)
                .ToList();

            var duplicateSets = new List<DuplicateCandidateSet>();
            int totalDuplicates = 0;
            ulong totalReclaimable = 0;

            byte[] headerBuffer = new byte[4096];

            foreach (var sizeGroup in sizeGroups)
            {
                // Phase 2: 4KB Header hash filter
                var headerGroups = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);

                foreach (var file in sizeGroup)
                {
                    try
                    {
                        using var fs = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        int read = fs.Read(headerBuffer, 0, (int)Math.Min((long)headerBuffer.Length, file.Length));
                        string headerHash = Convert.ToHexString(SHA256.HashData(headerBuffer.AsSpan(0, read)));

                        if (!headerGroups.TryGetValue(headerHash, out var list))
                        {
                            list = new List<FileInfo>();
                            headerGroups[headerHash] = list;
                        }
                        list.Add(file);
                    }
                    catch
                    {
                        // Inaccessible file skipped
                    }
                }

                // Phase 3: Full SHA-256 for header collisions
                foreach (var list in headerGroups.Values.Where(l => l.Count > 1))
                {
                    var fullHashGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                    foreach (var file in list)
                    {
                        try
                        {
                            using var fs = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            byte[] hashBytes = SHA256.HashData(fs);
                            string fullHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

                            if (!fullHashGroups.TryGetValue(fullHash, out var paths))
                            {
                                paths = new List<string>();
                                fullHashGroups[fullHash] = paths;
                            }
                            paths.Add(file.FullName);
                        }
                        catch
                        {
                            // Inaccessible file skipped
                        }
                    }

                    foreach (var (hash, paths) in fullHashGroups.Where(kvp => kvp.Value.Count > 1))
                    {
                        ulong reclaimable = (ulong)sizeGroup.Key * (ulong)(paths.Count - 1);
                        totalDuplicates += paths.Count;
                        totalReclaimable += reclaimable;

                        duplicateSets.Add(new DuplicateCandidateSet(
                            FullSha256: hash,
                            FileSizeBytes: sizeGroup.Key,
                            DuplicateCount: paths.Count,
                            ReclaimableBytes: reclaimable,
                            FilePaths: paths.AsReadOnly()));
                    }
                }
            }

            string summary = $"{candidateFiles.Count} files evaluated · {duplicateSets.Count} duplicate sets ({totalDuplicates} files) · {FormatBytes(totalReclaimable)} reclaimable";

            return new DuplicateScanReport(
                ScannedDirectory: directoryPath,
                FilesAudited: candidateFiles.Count,
                DuplicateGroupsCount: duplicateSets.Count,
                TotalDuplicateFiles: totalDuplicates,
                PotentialReclaimableBytes: totalReclaimable,
                FormattedReclaimableSize: FormatBytes(totalReclaimable),
                DuplicateSets: duplicateSets.AsReadOnly(),
                Summary: summary);
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
