using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Native;

namespace WinCare.Infrastructure.Commands;

/// <summary>
/// Developer artifact discovery and disk space reclamation engine.
/// Safely detects stale node_modules directory trees bounded by sibling package.json presence and reparse point exclusions.
/// </summary>
internal sealed partial class WindowsCommandExecutor
{
    internal static class DeveloperJunkHelper
    {
        private static readonly EnumerationOptions SafeOptions = new()
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
        };

        public static readonly IReadOnlyList<string> DefaultCandidateRoots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "repos"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "projects"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "dev"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "code"),
            @"C:\code",
            @"C:\projects",
            @"C:\dev",
        ];

        public static IReadOnlyList<StaleNodeModulesCandidate> ScanNodeModules(
            IEnumerable<string>? customRoots = null,
            int maxDepth = 4,
            int olderThanDays = 30)
        {
            var candidates = new List<StaleNodeModulesCandidate>();
            var roots = (customRoots ?? DefaultCandidateRoots).Where(Directory.Exists);
            DateTime cutoff = DateTime.UtcNow.AddDays(-olderThanDays);

            foreach (string root in roots)
            {
                var queue = new Queue<(string Path, int Depth)>();
                queue.Enqueue((root, 0));

                while (queue.Count > 0)
                {
                    (string currentDir, int depth) = queue.Dequeue();
                    if (depth > maxDepth) continue;

                    IEnumerable<string> subDirs;
                    try
                    {
                        subDirs = Directory.EnumerateDirectories(currentDir, "*", SafeOptions);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        continue;
                    }

                    foreach (string subDir in subDirs)
                    {
                        string dirName = Path.GetFileName(subDir);
                        if (string.Equals(dirName, "node_modules", StringComparison.OrdinalIgnoreCase))
                        {
                            // Safety invariant: Parent directory must contain package.json
                            string parentDir = Path.GetDirectoryName(subDir) ?? string.Empty;
                            string packageJson = Path.Combine(parentDir, "package.json");
                            if (File.Exists(packageJson))
                            {
                                DateTime lastWriteUtc = Directory.GetLastWriteTimeUtc(subDir);
                                bool isStale = lastWriteUtc <= cutoff;
                                candidates.Add(new StaleNodeModulesCandidate(
                                    subDir,
                                    parentDir,
                                    lastWriteUtc,
                                    isStale));
                            }
                        }
                        else if (!dirName.StartsWith('.') && !string.Equals(dirName, "bin", StringComparison.OrdinalIgnoreCase) && !string.Equals(dirName, "obj", StringComparison.OrdinalIgnoreCase))
                        {
                            queue.Enqueue((subDir, depth + 1));
                        }
                    }
                }
            }

            return candidates;
        }

        public static IReadOnlyList<ToolchainCacheCandidate> ScanToolchainCaches(
            IEnumerable<(string Ecosystem, string Path)>? customTargets = null)
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var targets = customTargets ??
            [
                ("NuGet-Http", Path.Combine(localAppData, "NuGet", "v3-cache")),
                ("NuGet-Packages", Path.Combine(userProfile, ".nuget", "packages")),
                ("Gradle-Caches", Path.Combine(userProfile, ".gradle", "caches")),
                ("Gradle-Daemon", Path.Combine(userProfile, ".gradle", "daemon")),
                ("Cargo-Cache", Path.Combine(userProfile, ".cargo", "registry", "cache")),
                ("Pip-Cache", Path.Combine(localAppData, "pip", "cache")),
                ("Npm-Cache", Path.Combine(localAppData, "npm-cache")),
                ("Pnpm-Store", Path.Combine(localAppData, "pnpm", "store")),
                ("Android-BuildCache", Path.Combine(userProfile, ".android", "build-cache")),
                ("JetBrains-Caches", Path.Combine(localAppData, "JetBrains")),
                ("VSCode-Cache", Path.Combine(appData, "Code", "Cache")),
                ("VSCode-CachedData", Path.Combine(appData, "Code", "CachedData")),
                ("PlatformIO-Cache", Path.Combine(userProfile, ".platformio", ".cache")),
                ("Electron-Cache", Path.Combine(localAppData, "electron")),
                ("Claude-Cache", Path.Combine(userProfile, ".claude")),
                ("Flutter-Cache", Path.Combine(localAppData, "Pub", "Cache")),
                ("HuggingFace-Hub", Path.Combine(userProfile, ".cache", "huggingface", "hub")),
                ("Ollama-Models", Path.Combine(userProfile, ".ollama", "models")),
                ("PyTorch-Hub", Path.Combine(userProfile, ".cache", "torch", "hub", "checkpoints")),
                ("LMStudio-Cache", Path.Combine(appData, "LM Studio", "Cache")),
                ("CherryStudio-Cache", Path.Combine(appData, "CherryStudio", "Cache")),
                ("Jan-Cache", Path.Combine(appData, "Jan", "Cache")),
                ("AICubby-Cache", Path.Combine(localAppData, "cubby", "Cache")),
                ("AICubby-Models", Path.Combine(userProfile, ".cache", "cubby", "models")),
                ("Android-WSA", Path.Combine(localAppData, "Packages", "MicrosoftCorporationII.WindowsSubsystemForAndroid_8wekyb3d8bbwe", "LocalCache")),
                ("Android-AvdCache", Path.Combine(userProfile, ".android", "avd")),
                ("Redis-Data", Path.Combine(localAppData, "Redis")),
            ];

            var results = new List<ToolchainCacheCandidate>();
            foreach (var (ecosystem, path) in targets)
            {
                bool exists = false;
                try
                {
                    if (Directory.Exists(path))
                    {
                        var dirInfo = new DirectoryInfo(path);
                        if ((dirInfo.Attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            exists = true;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    exists = false;
                }

                results.Add(new ToolchainCacheCandidate(ecosystem, path, exists));
            }
            return results;
        }

        public static IReadOnlyList<string> ScanAdbCaches()
        {
            var results = new List<string>();
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string androidDir = Path.Combine(userProfile, ".android");
            if (Directory.Exists(androidDir))
            {
                results.Add(androidDir);
            }

            string packagesDir = Path.Combine(localAppData, "Packages");
            if (Directory.Exists(packagesDir))
            {
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(packagesDir, "*WindowsSubsystemForAndroid*"))
                    {
                        string localCache = Path.Combine(dir, "LocalCache");
                        if (Directory.Exists(localCache)) results.Add(localCache);
                    }
                }
                catch { }
            }
            return results;
        }

        public static IReadOnlyList<string> ScanRedisCaches(IEnumerable<string>? candidateRoots = null)
        {
            var results = new List<string>();
            var roots = (candidateRoots ?? DefaultCandidateRoots).Where(Directory.Exists);
            foreach (string root in roots)
            {
                try
                {
                    foreach (string file in Directory.EnumerateFiles(root, "*.rdb", SearchOption.TopDirectoryOnly))
                    {
                        results.Add(file);
                    }
                    foreach (string file in Directory.EnumerateFiles(root, "*.aof", SearchOption.TopDirectoryOnly))
                    {
                        results.Add(file);
                    }
                }
                catch { }
            }
            return results;
        }

        public static IReadOnlyList<string> ScanIncompleteDownloads(int olderThanDays = 7)
        {
            var results = new List<string>();
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(downloads)) return results;

            DateTime cutoff = DateTime.UtcNow.AddDays(-olderThanDays);
            string[] patterns =
            [
                "*.crdownload",
                "*.part",
                "*.aria2",
                "*.dap",
                "*.abdm.part",
                "*.gopeed",
                "*.qdm",
                "*.dlman.part",
                "*.cpdm.part",
                "*.chunk",
                "*.motrix"
            ];
            foreach (string pattern in patterns)
            {
                try
                {
                    foreach (string file in Directory.EnumerateFiles(downloads, pattern, SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            if (File.GetLastWriteTimeUtc(file) < cutoff)
                            {
                                results.Add(file);
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return results;
        }

        private static readonly HashSet<string> DosReservedStems = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        private static readonly char[] IllegalFileNameChars = Path.GetInvalidFileNameChars();

        public static DownloadFilenameSanitizationResult SanitizeDownloadFilename(string? rawFilename, int maxLength = 180)
        {
            if (string.IsNullOrWhiteSpace(rawFilename))
            {
                return new DownloadFilenameSanitizationResult(
                    Original: rawFilename ?? string.Empty,
                    Sanitized: "download.bin",
                    WasModified: true,
                    DosStemDetected: false);
            }

            string working = rawFilename.Trim();
            bool modified = false;
            bool dosDetected = false;

            var sb = new System.Text.StringBuilder(working.Length);
            foreach (char c in working)
            {
                if (c < 32 || IllegalFileNameChars.Contains(c))
                {
                    sb.Append('_');
                    modified = true;
                }
                else
                {
                    sb.Append(c);
                }
            }
            working = sb.ToString().Trim(' ', '.');
            if (string.IsNullOrWhiteSpace(working))
            {
                working = "download.bin";
                modified = true;
            }

            string extension = Path.GetExtension(working);
            string stem = Path.GetFileNameWithoutExtension(working);

            if (DosReservedStems.Contains(stem))
            {
                dosDetected = true;
                stem = "_" + stem;
                working = stem + extension;
                modified = true;
            }

            if (working.Length > maxLength && maxLength > extension.Length + 4)
            {
                int maxStemLength = maxLength - extension.Length;
                stem = stem.Substring(0, Math.Min(stem.Length, maxStemLength)).TrimEnd(' ', '.');
                working = stem + extension;
                modified = true;
            }

            return new DownloadFilenameSanitizationResult(
                Original: rawFilename,
                Sanitized: working,
                WasModified: modified || working != rawFilename,
                DosStemDetected: dosDetected);
        }

        public static DownloadFreeSpaceResult ValidateDownloadFreeSpace(string targetPath, long requiredBytes, long safetyBufferBytes = 104857600)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(targetPath)) ?? "C:\\";
                var drive = new DriveInfo(root);
                long available = drive.AvailableFreeSpace;
                long totalNeeded = requiredBytes + safetyBufferBytes;
                bool isSufficient = available >= totalNeeded;

                return new DownloadFreeSpaceResult(
                    DriveName: drive.Name,
                    AvailableBytes: available,
                    RequiredBytes: requiredBytes,
                    SafetyBufferBytes: safetyBufferBytes,
                    IsSufficient: isSufficient,
                    Summary: isSufficient
                        ? $"Drive {drive.Name} has sufficient free space ({available / (1024 * 1024)} MB available >= {totalNeeded / (1024 * 1024)} MB needed)."
                        : $"Insufficient free space on drive {drive.Name}: {available / (1024 * 1024)} MB available < {totalNeeded / (1024 * 1024)} MB needed.");
            }
            catch (Exception ex)
            {
                return new DownloadFreeSpaceResult(
                    DriveName: targetPath,
                    AvailableBytes: 0,
                    RequiredBytes: requiredBytes,
                    SafetyBufferBytes: safetyBufferBytes,
                    IsSufficient: false,
                    Summary: $"Failed to assess drive free space for '{targetPath}': {ex.Message}");
            }
        }

        public static bool IsRetryableDownloadStatus(int statusCode) =>
            statusCode is 408 or 429 or 500 or 502 or 503 or 504;

        public static DownloadCategoryClassification CategorizeDownloadFile(string? filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                return new DownloadCategoryClassification(DownloadFileCategory.Other, string.Empty, "Other");
            }

            string ext = Path.GetExtension(filename).TrimStart('.').ToLowerInvariant();
            var category = ext switch
            {
                "zip" or "rar" or "7z" or "tar" or "gz" or "bz2" or "xz" or "zst" or "cab" or "iso" or "dmg" or "img" or "tgz"
                    => DownloadFileCategory.Compressed,

                "exe" or "msi" or "apk" or "appimage" or "deb" or "rpm" or "bin" or "bat" or "sh" or "jar" or "app"
                    => DownloadFileCategory.Programs,

                "mp4" or "avi" or "mkv" or "mov" or "wmv" or "flv" or "webm" or "m4v" or "3gp" or "mpeg" or "ts" or "m3u8"
                    => DownloadFileCategory.Videos,

                "mp3" or "wav" or "aac" or "flac" or "ogg" or "aiff" or "wma" or "m4a" or "opus"
                    => DownloadFileCategory.Music,

                "jpg" or "jpeg" or "png" or "gif" or "bmp" or "tiff" or "tif" or "svg" or "webp" or "heic" or "ico" or "raw" or "psd"
                    => DownloadFileCategory.Pictures,

                "doc" or "docx" or "pdf" or "txt" or "rtf" or "odt" or "xls" or "xlsx" or "ppt" or "pptx" or "csv" or "epub" or "mobi" or "pages"
                    => DownloadFileCategory.Documents,

                _ => DownloadFileCategory.Other
            };

            string subfolder = category switch
            {
                DownloadFileCategory.Compressed => "Compressed",
                DownloadFileCategory.Programs => "Programs",
                DownloadFileCategory.Videos => "Videos",
                DownloadFileCategory.Music => "Music",
                DownloadFileCategory.Pictures => "Pictures",
                DownloadFileCategory.Documents => "Documents",
                _ => "Other"
            };

            return new DownloadCategoryClassification(category, ext, subfolder);
        }

        public static IReadOnlyList<DownloadChunkRange> CalculateDownloadChunks(
            long totalFileSize,
            int threadCount,
            long minimumChunkSize = 1048576)
        {
            if (totalFileSize <= 0) return Array.Empty<DownloadChunkRange>();

            int safeThreads = Math.Max(1, threadCount);
            if (minimumChunkSize > 0)
            {
                int maxChunks = (int)(totalFileSize / minimumChunkSize);
                if (maxChunks < 1) maxChunks = 1;
                safeThreads = Math.Min(safeThreads, maxChunks);
            }

            long baseChunkSize = totalFileSize / safeThreads;
            var chunks = new List<DownloadChunkRange>(safeThreads);

            for (int i = 0; i < safeThreads; i++)
            {
                long start = i * baseChunkSize;
                long end = (i == safeThreads - 1) ? (totalFileSize - 1) : (start + baseChunkSize - 1);
                long size = end - start + 1;
                chunks.Add(new DownloadChunkRange(i, start, end, size));
            }

            return chunks;
        }

        public static MultiPartArchiveInfo DetectMultiPartArchiveGroup(string? filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                return new MultiPartArchiveInfo(false, string.Empty, 0, false);
            }

            string name = filename.Trim();

            // Match .part01.rar / .part1.rar
            var partRarMatch = System.Text.RegularExpressions.Regex.Match(
                name,
                @"^(.*?)\.part(\d+)\.rar$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (partRarMatch.Success)
            {
                int partNum = int.Parse(partRarMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                return new MultiPartArchiveInfo(true, partRarMatch.Groups[1].Value, partNum, partNum <= 1);
            }

            // Match .7z.001 / .tar.gz.001 / .001
            var numericPartMatch = System.Text.RegularExpressions.Regex.Match(
                name,
                @"^(.*?)\.(\d{3})$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (numericPartMatch.Success)
            {
                int partNum = int.Parse(numericPartMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                return new MultiPartArchiveInfo(true, numericPartMatch.Groups[1].Value, partNum, partNum <= 1);
            }

            // Match .z01 / .r00 / .r01
            var splitZipMatch = System.Text.RegularExpressions.Regex.Match(
                name,
                @"^(.*?)\.[zr](\d{2})$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (splitZipMatch.Success)
            {
                int partNum = int.Parse(splitZipMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                return new MultiPartArchiveInfo(true, splitZipMatch.Groups[1].Value, partNum, partNum <= 1);
            }

            return new MultiPartArchiveInfo(false, name, 0, false);
        }

        public static bool IsSignedUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            string lower = url.ToLowerInvariant();
            return lower.Contains("x-amz-expires")
                || lower.Contains("x-amz-credential")
                || lower.Contains("x-amz-date")
                || lower.Contains("&se=") || lower.Contains("?se=")
                || lower.Contains("&expires=") || lower.Contains("?expires=")
                || lower.Contains("x-goog-signature");
        }

        public static bool IsSameVolume(string pathA, string pathB)
        {
            try
            {
                string rootA = Path.GetPathRoot(Path.GetFullPath(pathA)) ?? string.Empty;
                string rootB = Path.GetPathRoot(Path.GetFullPath(pathB)) ?? string.Empty;
                return string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static ResourceHealthReport CheckLocalResourceHealth(IReadOnlyList<string> candidatePaths)
        {
            if (candidatePaths == null || candidatePaths.Count == 0)
            {
                return new ResourceHealthReport(0, 0, 0, Array.Empty<string>());
            }

            var missing = new List<string>();
            int existing = 0;

            foreach (string path in candidatePaths)
            {
                try
                {
                    if (File.Exists(path) || Directory.Exists(path))
                    {
                        existing++;
                    }
                    else
                    {
                        missing.Add(path);
                    }
                }
                catch
                {
                    missing.Add(path);
                }
            }

            return new ResourceHealthReport(candidatePaths.Count, existing, missing.Count, missing);
        }

        public static IReadOnlyList<RelocationMatch> RelocateMissingFiles(
            IReadOnlyList<string> missingPaths,
            IReadOnlyList<string> knownDirectories)
        {
            if (missingPaths == null || missingPaths.Count == 0 || knownDirectories == null || knownDirectories.Count == 0)
            {
                return Array.Empty<RelocationMatch>();
            }

            var matches = new List<RelocationMatch>();

            foreach (string missing in missingPaths)
            {
                string filename = Path.GetFileName(missing);
                if (string.IsNullOrWhiteSpace(filename)) continue;

                var candidateMatches = new List<string>();

                foreach (string dir in knownDirectories)
                {
                    if (!Directory.Exists(dir)) continue;
                    if (!IsSameVolume(missing, dir)) continue;

                    string candidateFile = Path.Combine(dir, filename);
                    if (File.Exists(candidateFile))
                    {
                        candidateMatches.Add(candidateFile);
                    }
                }

                if (candidateMatches.Count == 1)
                {
                    string matchPath = candidateMatches[0];
                    long size = 0;
                    try
                    {
                        size = new FileInfo(matchPath).Length;
                    }
                    catch { }

                    matches.Add(new RelocationMatch(missing, matchPath, size));
                }
            }

            return matches;
        }

        public static class TokenBucketThrottlerCalculator
        {
            public static long BytesToNanos(long bytes, long bytesPerSecond)
            {
                return bytesPerSecond > 0 ? (bytes * 1_000_000_000L / bytesPerSecond) : 0L;
            }

            public static long NanosToBytes(long nanos, long bytesPerSecond)
            {
                return bytesPerSecond > 0 ? (nanos * bytesPerSecond / 1_000_000_000L) : long.MaxValue;
            }

            public static ThrottlerAllocationResult CalculateAllocation(
                long nowNanos,
                long allocatedUntilNanos,
                long requestedBytes,
                long bytesPerSecond,
                long maxBurstBytes = 262144,
                long minWaitBytes = 8192)
            {
                if (bytesPerSecond <= 0)
                {
                    return new ThrottlerAllocationResult(
                        GrantedBytes: requestedBytes,
                        WaitNanos: 0,
                        NewAllocatedUntilNanos: nowNanos,
                        CanProceedImmediately: true);
                }

                long idleInNanos = Math.Max(0L, allocatedUntilNanos - nowNanos);
                long immediateBytes = maxBurstBytes - NanosToBytes(idleInNanos, bytesPerSecond);

                if (immediateBytes >= requestedBytes)
                {
                    long newAllocated = nowNanos + idleInNanos + BytesToNanos(requestedBytes, bytesPerSecond);
                    return new ThrottlerAllocationResult(
                        GrantedBytes: requestedBytes,
                        WaitNanos: 0,
                        NewAllocatedUntilNanos: newAllocated,
                        CanProceedImmediately: true);
                }

                if (immediateBytes >= minWaitBytes)
                {
                    long newAllocated = nowNanos + BytesToNanos(maxBurstBytes, bytesPerSecond);
                    return new ThrottlerAllocationResult(
                        GrantedBytes: immediateBytes,
                        WaitNanos: 0,
                        NewAllocatedUntilNanos: newAllocated,
                        CanProceedImmediately: true);
                }

                long minBytes = Math.Min(minWaitBytes, requestedBytes);
                long minWaitNanos = idleInNanos + BytesToNanos(minBytes - maxBurstBytes, bytesPerSecond);

                if (minWaitNanos <= 0)
                {
                    long newAllocated = nowNanos + BytesToNanos(maxBurstBytes, bytesPerSecond);
                    return new ThrottlerAllocationResult(
                        GrantedBytes: minBytes,
                        WaitNanos: 0,
                        NewAllocatedUntilNanos: newAllocated,
                        CanProceedImmediately: true);
                }

                return new ThrottlerAllocationResult(
                    GrantedBytes: 0,
                    WaitNanos: minWaitNanos,
                    NewAllocatedUntilNanos: allocatedUntilNanos,
                    CanProceedImmediately: false);
            }
        }

        public static bool ValidateConfigurationStructure(string content, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(content))
            {
                error = "Configuration content is empty.";
                return false;
            }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(content);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object && doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    error = "Root element must be an object or array.";
                    return false;
                }
                return true;
            }
            catch (System.Text.Json.JsonException)
            {
                var lines = content.Split('\n');
                int depth = 0;
                foreach (var rawLine in lines)
                {
                    string line = rawLine.TrimEnd('\r');
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;

                    int indent = line.Length - line.TrimStart().Length;
                    if (indent % 2 != 0 && indent % 4 != 0 && indent > 0)
                    {
                        error = $"Inconsistent indentation at line: {line.Trim()}";
                        return false;
                    }
                    depth = Math.Max(depth, indent);
                }
                if (depth > 20)
                {
                    error = "Configuration nesting exceeds maximum depth limit of 20.";
                    return false;
                }
                return true;
            }
        }

        public static BandwidthEstimate CalculateRollingBandwidth(
                long totalBytesTransferred,
                long previousBytesTransferred,
                double elapsedSeconds,
                double previousBps = 0.0,
                double smoothingFactorAlpha = 0.25,
                long totalFileBytes = -1)
            {
                if (elapsedSeconds <= 0.0001)
                {
                    return new BandwidthEstimate(previousBps, previousBps, 0, false);
                }

                long deltaBytes = Math.Max(0, totalBytesTransferred - previousBytesTransferred);
                double instantaneousBps = (double)deltaBytes / elapsedSeconds;
                double smoothedBps = previousBps <= 0.0
                    ? instantaneousBps
                    : (smoothingFactorAlpha * instantaneousBps) + ((1.0 - smoothingFactorAlpha) * previousBps);

                long etaSeconds = 0;
                bool hasEta = false;
                if (totalFileBytes > totalBytesTransferred && smoothedBps > 1.0)
                {
                    long remaining = totalFileBytes - totalBytesTransferred;
                    etaSeconds = (long)Math.Ceiling(remaining / smoothedBps);
                    hasEta = true;
                }

                return new BandwidthEstimate(instantaneousBps, smoothedBps, etaSeconds, hasEta);
            }

            public static ContentRangeHeaderInfo ParseContentRangeHeader(string? headerValue)
            {
                if (string.IsNullOrWhiteSpace(headerValue))
                {
                    return new ContentRangeHeaderInfo(false, 0, 0, -1, "Missing header");
                }

                string trimmed = headerValue.Trim();
                if (!trimmed.StartsWith("bytes ", StringComparison.OrdinalIgnoreCase))
                {
                    return new ContentRangeHeaderInfo(false, 0, 0, -1, "Invalid unit");
                }

                string spec = trimmed.Substring(6).Trim();
                string[] parts = spec.Split('/');
                if (parts.Length != 2)
                {
                    return new ContentRangeHeaderInfo(false, 0, 0, -1, "Invalid range/total separator");
                }

                long total = -1;
                if (parts[1].Trim() != "*")
                {
                    if (!long.TryParse(parts[1].Trim(), out total)) total = -1;
                }

                if (parts[0].Trim() == "*")
                {
                    return new ContentRangeHeaderInfo(true, 0, 0, total, "Unsatisfied range query");
                }

                string[] rangeParts = parts[0].Split('-');
                if (rangeParts.Length != 2 ||
                    !long.TryParse(rangeParts[0].Trim(), out long start) ||
                    !long.TryParse(rangeParts[1].Trim(), out long end))
                {
                    return new ContentRangeHeaderInfo(false, 0, 0, total, "Invalid byte range offsets");
                }

                return new ContentRangeHeaderInfo(true, start, end, total, "Parsed successfully");
            }

            public static bool IsHelperOrInstallerBinary(string filePath)
            {
                if (string.IsNullOrWhiteSpace(filePath)) return false;
                string fileName = Path.GetFileName(filePath);
                if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;

                string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();

                string[] prefixesOrKeywords =
                [
                    "uninstall",
                    "setup",
                    "install",
                    "update",
                    "updater",
                    "crash",
                    "crashpad",
                    "repair",
                    "squirrel",
                    "migrate",
                    "redist",
                    "vcredist",
                    "dxsetup",
                    "directx"
                ];

                foreach (string kw in prefixesOrKeywords)
                {
                    if (nameWithoutExt.Contains(kw, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }

            public static SteamGameInspectionResult? DetectSteamGameInstall(string steamAppsDirectory, string installDirName)
            {
                if (string.IsNullOrWhiteSpace(steamAppsDirectory) || !Directory.Exists(steamAppsDirectory))
                    return null;

                string searchDir = installDirName.Trim().ToLowerInvariant();

                try
                {
                    foreach (string acfPath in Directory.EnumerateFiles(steamAppsDirectory, "appmanifest_*.acf"))
                    {
                        string content = File.ReadAllText(acfPath);
                        var installDirMatch = System.Text.RegularExpressions.Regex.Match(
                            content,
                            @"""installdir""\s+""([^""]*)""",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                        if (installDirMatch.Success && installDirMatch.Groups[1].Value.Equals(searchDir, StringComparison.OrdinalIgnoreCase))
                        {
                            var appIdMatch = System.Text.RegularExpressions.Regex.Match(
                                content,
                                @"""appid""\s+""([^""]*)""",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            var nameMatch = System.Text.RegularExpressions.Regex.Match(
                                content,
                                @"""name""\s+""([^""]*)""",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                            string appId = appIdMatch.Success ? appIdMatch.Groups[1].Value : "Unknown";
                            string name = nameMatch.Success ? nameMatch.Groups[1].Value : installDirName;

                            string libraryCacheDir = Path.Combine(steamAppsDirectory, "librarycache");
                            string? coverPath = null;

                            string[] coverCandidates =
                            [
                                $"{appId}_header.jpg",
                                $"{appId}_library_600x900.jpg",
                                $"{appId}_logo.png"
                            ];

                            foreach (string candidate in coverCandidates)
                            {
                                string candidatePath = Path.Combine(libraryCacheDir, candidate);
                                if (File.Exists(candidatePath))
                                {
                                    coverPath = candidatePath;
                                    break;
                                }
                            }

                            return new SteamGameInspectionResult(appId, name, installDirName, acfPath, coverPath);
                        }
                    }
                }
                catch
                {
                    // Safe fail-closed
                }

                return null;
            }

            public static double ComputeAffinityDecay(
                double currentScore,
                long elapsedMilliseconds,
                long halfLifeMilliseconds = 90L * 24 * 60 * 60 * 1000)
            {
                if (double.IsNaN(currentScore) || double.IsInfinity(currentScore) || currentScore <= 0.0)
                    return 0.0;

                if (elapsedMilliseconds <= 0)
                    return currentScore;

                double factor = Math.Pow(0.5, (double)elapsedMilliseconds / Math.Max(1, halfLifeMilliseconds));
                return currentScore * factor;
            }

            public static double ApplyAffinityFeedback(double currentScore, bool wasDirectlyInteracted, double maxCeiling = 4.0)
            {
                double baseScore = currentScore <= 0 ? 1.0 : currentScore;
                double gain = wasDirectlyInteracted ? 0.9 : 0.65;
                return Math.Min(baseScore + gain, maxCeiling);
            }

            public static bool SetSparseFile(string filePath)
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;

                try
                {
                    using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                    nint handle = fileStream.SafeFileHandle.DangerousGetHandle();
                    bool success = WindowsInterop.DeviceIoControl(
                        handle,
                        WindowsInterop.FsctlSetSparse,
                        0,
                        0,
                        0,
                        0,
                        out _,
                        0);
                    return success;
                }
                catch
                {
                    return false;
                }
            }

            public static bool CanVolumeSupportSparseFiles(string path)
            {
                if (string.IsNullOrWhiteSpace(path)) return false;
                try
                {
                    string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? string.Empty;
                    if (string.IsNullOrEmpty(root)) return false;
                    var driveInfo = new DriveInfo(root);
                    string fs = driveInfo.DriveFormat.ToLowerInvariant();
                    return fs is "ntfs" or "refs";
                }
                catch
                {
                    return false;
                }
            }

            public static PathSizeResult GetPathSizeBounded(string path, int maxEntries = 10000, int maxDurationMs = 1500)
            {
                if (string.IsNullOrWhiteSpace(path) || (!Directory.Exists(path) && !File.Exists(path)))
                {
                    return new PathSizeResult(0, 0, false, "Path does not exist");
                }

                if (File.Exists(path))
                {
                    long len = new FileInfo(path).Length;
                    return new PathSizeResult(len, 1, false, "Single file");
                }

                long totalBytes = 0;
                int entriesCount = 0;
                bool truncated = false;
                long deadlineTicks = Stopwatch.GetTimestamp() + (long)((double)maxDurationMs / 1000.0 * Stopwatch.Frequency);

                var queue = new Queue<string>();
                queue.Enqueue(path);

                while (queue.Count > 0)
                {
                    if (entriesCount >= maxEntries || Stopwatch.GetTimestamp() > deadlineTicks)
                    {
                        truncated = true;
                        break;
                    }

                    string current = queue.Dequeue();
                    try
                    {
                        var dirInfo = new DirectoryInfo(current);
                        if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0) continue;

                        foreach (var fi in dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                        {
                            if (entriesCount++ >= maxEntries || Stopwatch.GetTimestamp() > deadlineTicks)
                            {
                                truncated = true;
                                break;
                            }
                            if ((fi.Attributes & FileAttributes.ReparsePoint) == 0)
                            {
                                totalBytes += fi.Length;
                            }
                        }

                        if (truncated) break;

                        foreach (var di in dirInfo.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
                        {
                            if (entriesCount++ >= maxEntries || Stopwatch.GetTimestamp() > deadlineTicks)
                            {
                                truncated = true;
                                break;
                            }
                            if ((di.Attributes & FileAttributes.ReparsePoint) == 0)
                            {
                                queue.Enqueue(di.FullName);
                            }
                        }
                    }
                    catch
                    {
                        // Skip inaccessible directories
                    }
                }

                return new PathSizeResult(totalBytes, entriesCount, truncated, truncated ? "Budget ceiling reached" : "Complete scan");
            }

            public static string GetDeduplicatedFilePath(string targetPath)
            {
                if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
                    return targetPath;

                string dir = Path.GetDirectoryName(targetPath) ?? string.Empty;
                string nameWithoutExt = Path.GetFileNameWithoutExtension(targetPath);
                string ext = Path.GetExtension(targetPath);

                int counter = 1;
                while (counter < 10000)
                {
                    string candidate = Path.Combine(dir, $"{nameWithoutExt}_{counter}{ext}");
                    if (!File.Exists(candidate))
                        return candidate;
                    counter++;
                }

                return Path.Combine(dir, $"{nameWithoutExt}_{Guid.NewGuid():N}{ext}");
            }

            public static string FormatFileUri(string localOrUncPath)
            {
                if (string.IsNullOrWhiteSpace(localOrUncPath)) return string.Empty;
                string forward = localOrUncPath.Replace('\\', '/');
                if (forward.StartsWith("//"))
                {
                    return "file:////" + forward.Substring(2);
                }
                if (forward.Length >= 2 && forward[1] == ':')
                {
                    return "file:///" + forward;
                }
                return "file:///" + forward.TrimStart('/');
            }

            public static TrackerSanitizationResult SanitizeTrackerList(IEnumerable<string>? rawTrackers, int maxBufferLength = 2048)
            {
                if (rawTrackers == null)
                    return new TrackerSanitizationResult([], string.Empty, string.Empty, 0);

                var validTrackers = new List<string>();
                var seenHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int dropped = 0;

                foreach (var line in rawTrackers)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string cleaned = line.Trim();
                    if (cleaned.StartsWith('#') || cleaned.StartsWith("//"))
                        continue;

                    string[] parts = cleaned.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        string candidate = part.Trim();
                        if (string.IsNullOrEmpty(candidate)) continue;

                        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
                            (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                             uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
                             uri.Scheme.Equals("udp", StringComparison.OrdinalIgnoreCase) ||
                             uri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase)))
                        {
                            string key = $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}";
                            if (seenHosts.Add(key))
                            {
                                validTrackers.Add(candidate);
                            }
                            else
                            {
                                dropped++;
                            }
                        }
                        else
                        {
                            dropped++;
                        }
                    }
                }

                string lineSep = string.Join("\r\n", validTrackers);
                string commaSep = string.Join(",", validTrackers);

                if (commaSep.Length > maxBufferLength && maxBufferLength > 0)
                {
                    int lastComma = commaSep.LastIndexOf(',', maxBufferLength);
                    if (lastComma > 0)
                    {
                        commaSep = commaSep.Substring(0, lastComma);
                    }
                    else
                    {
                        commaSep = commaSep.Substring(0, maxBufferLength);
                    }
                }

                return new TrackerSanitizationResult(validTrackers, commaSep, lineSep, dropped);
            }

            public static IReadOnlyList<string> ExtractDownloadUrls(string? clipboardText)
            {
                if (string.IsNullOrWhiteSpace(clipboardText))
                    return [];

                var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var magnetMatches = System.Text.RegularExpressions.Regex.Matches(
                    clipboardText,
                    @"magnet:\?xt=urn:btih:[a-zA-Z0-9]+[^\s""'>]*",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(100));

                foreach (System.Text.RegularExpressions.Match m in magnetMatches)
                {
                    if (m.Success) urls.Add(m.Value);
                }

                var fileMatches = System.Text.RegularExpressions.Regex.Matches(
                    clipboardText,
                    @"(https?|ftps?)://[^\s""'>]+\.(exe|msi|zip|rar|7z|tar\.gz|iso|img|mp4|mkv|pdf|bin|whl|safetensors|gguf)([?][^\s""'>]*)?",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(100));

                foreach (System.Text.RegularExpressions.Match m in fileMatches)
                {
                    if (m.Success)
                    {
                        string raw = m.Value;
                        int qIdx = raw.IndexOf('?');
                        if (qIdx > 0)
                        {
                            string query = raw.Substring(qIdx + 1);
                            var keepPairs = new List<string>();
                            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                            {
                                if (!pair.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) &&
                                    !pair.StartsWith("fbclid", StringComparison.OrdinalIgnoreCase) &&
                                    !pair.StartsWith("ref=", StringComparison.OrdinalIgnoreCase))
                                {
                                    keepPairs.Add(pair);
                                }
                            }
                            raw = keepPairs.Count > 0 ? $"{raw.Substring(0, qIdx)}?{string.Join('&', keepPairs)}" : raw.Substring(0, qIdx);
                        }
                        urls.Add(raw);
                    }
                }

                return urls.ToList();
            }

            public static ModelMemoryFitResult EstimateModelMemoryFit(
                long parameterCount,
                int quantizationBits,
                int contextTokens,
                long availableVramBytes,
                long availableRamBytes,
                int layerCount = 32)
            {
                parameterCount = Math.Max(1, parameterCount);
                quantizationBits = Math.Clamp(quantizationBits, 2, 32);
                contextTokens = Math.Max(128, contextTokens);
                layerCount = Math.Clamp(layerCount, 8, 128);

                long weightBytes = (long)Math.Ceiling((double)parameterCount * quantizationBits / 8.0);
                int hiddenDim = Math.Clamp(layerCount * 128, 2048, 8192);
                long kvCacheBytes = (long)layerCount * 2 * hiddenDim * contextTokens * 2;
                long activationBytes = (long)(weightBytes * 0.15);
                long totalRequiredBytes = weightBytes + kvCacheBytes + activationBytes;

                ModelFitClassification classification;
                string explanation;

                long vramBudget = (long)(availableVramBytes * 0.90);
                long ramBudget = (long)(availableRamBytes * 0.85);

                if (availableVramBytes > 0 && totalRequiredBytes <= vramBudget)
                {
                    classification = ModelFitClassification.FitsVramFully;
                    explanation = $"Model weights ({weightBytes / 1048576} MB) and KV cache ({kvCacheBytes / 1048576} MB) fit entirely in available VRAM ({availableVramBytes / 1048576} MB).";
                }
                else if (availableVramBytes > 0 && weightBytes <= vramBudget)
                {
                    classification = ModelFitClassification.PartialOffloadGpu;
                    explanation = $"Model weights ({weightBytes / 1048576} MB) fit in VRAM, but context KV cache ({kvCacheBytes / 1048576} MB) requires system RAM offloading.";
                }
                else if (totalRequiredBytes <= ramBudget)
                {
                    classification = ModelFitClassification.CpuRamOnly;
                    explanation = $"Model exceeds available GPU VRAM, but fits fully in system CPU RAM ({availableRamBytes / 1048576} MB). High-latency CPU inference required.";
                }
                else
                {
                    classification = ModelFitClassification.InsufficientMemory;
                    explanation = $"Total requirement ({totalRequiredBytes / 1048576} MB) exceeds safe operating memory ceilings for both VRAM ({availableVramBytes / 1048576} MB) and system RAM ({availableRamBytes / 1048576} MB).";
                }

                return new ModelMemoryFitResult(
                    parameterCount,
                    quantizationBits,
                    weightBytes,
                    kvCacheBytes,
                    activationBytes,
                    totalRequiredBytes,
                    classification,
                    explanation);
            }

            public static ServerRangeSupportInfo ValidateServerRangeSupport(
                string? acceptRangesHeader,
                string? contentRangeHeader,
                long? contentLength)
            {
                bool hasByteRanges = false;
                if (!string.IsNullOrWhiteSpace(acceptRangesHeader) &&
                    acceptRangesHeader.Contains("bytes", StringComparison.OrdinalIgnoreCase))
                {
                    hasByteRanges = true;
                }
                else if (!string.IsNullOrWhiteSpace(contentRangeHeader) &&
                         contentRangeHeader.StartsWith("bytes", StringComparison.OrdinalIgnoreCase))
                {
                    hasByteRanges = true;
                }

                bool isIndeterminate = !contentLength.HasValue || contentLength.Value <= 0;
                long totalBytes = contentLength.GetValueOrDefault(-1);

                if (hasByteRanges && !string.IsNullOrWhiteSpace(contentRangeHeader))
                {
                    var parsed = ParseContentRangeHeader(contentRangeHeader);
                    if (parsed.IsValid && parsed.TotalBytes > 0)
                    {
                        totalBytes = parsed.TotalBytes;
                        isIndeterminate = false;
                    }
                }

                string mode = hasByteRanges
                    ? (isIndeterminate ? "ByteRangesSupported_ChunkedStream" : "ByteRangesSupported_FixedLength")
                    : "SingleStreamOnly_NonResumable";

                return new ServerRangeSupportInfo(hasByteRanges, isIndeterminate, totalBytes, mode);
            }

            public sealed class TokenBucketRateLimiter
            {
                private readonly object _syncLock = new();
                private long _capacity;
                private double _tokens;
                private long _refillRate;
                private long _lastRefillTicks;
                private bool _isUnlimited;
                private long _transferredBytes;
                private long _allocatedUntilNanos;

                public TokenBucketRateLimiter(long bytesPerSecond)
                {
                    SetRateLimit(bytesPerSecond, resetTokens: true);
                }

                public static TokenBucketRateLimiter Unlimited() => new(0);

                public long Capacity => _capacity;
                public long RefillRate => _refillRate;
                public bool IsUnlimited => _isUnlimited;
                public long TransferredBytes => Interlocked.Read(ref _transferredBytes);

                public void SetRateLimit(long bytesPerSecond, bool resetTokens = false)
                {
                    lock (_syncLock)
                    {
                        if (bytesPerSecond <= 0 || bytesPerSecond == long.MaxValue)
                        {
                            _isUnlimited = true;
                            _capacity = long.MaxValue;
                            _refillRate = long.MaxValue;
                            _tokens = long.MaxValue;
                        }
                        else
                        {
                            _isUnlimited = false;
                            _capacity = bytesPerSecond;
                            _refillRate = bytesPerSecond;
                            _tokens = resetTokens ? bytesPerSecond : Math.Min(_tokens, (double)bytesPerSecond);
                        }
                        _allocatedUntilNanos = 0;
                        _lastRefillTicks = Stopwatch.GetTimestamp();
                    }
                }

                public bool TryAcquire(long bytes)
                {
                    if (bytes <= 0) return true;
                    if (_isUnlimited)
                    {
                        Interlocked.Add(ref _transferredBytes, bytes);
                        return true;
                    }

                    lock (_syncLock)
                    {
                        RefillTokens();
                        if (_tokens >= bytes)
                        {
                            _tokens -= bytes;
                            Interlocked.Add(ref _transferredBytes, bytes);
                            return true;
                        }
                        return false;
                    }
                }

                public void Acquire(long bytes, CancellationToken cancellationToken = default)
                {
                    if (bytes <= 0) return;
                    if (_isUnlimited)
                    {
                        Interlocked.Add(ref _transferredBytes, bytes);
                        return;
                    }

                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        double waitMs = 0;

                        lock (_syncLock)
                        {
                            RefillTokens();
                            if (_tokens >= bytes)
                            {
                                _tokens -= bytes;
                                Interlocked.Add(ref _transferredBytes, bytes);
                                return;
                            }

                            double needed = bytes - _tokens;
                            waitMs = (needed / _refillRate) * 1000.0;
                        }

                        int sleepTime = Math.Clamp((int)Math.Ceiling(waitMs), 1, 50);
                        Thread.Sleep(sleepTime);
                    }
                }

                private void RefillTokens()
                {
                    if (_isUnlimited) return;

                    long now = Stopwatch.GetTimestamp();
                    double elapsedSeconds = (double)(now - _lastRefillTicks) / Stopwatch.Frequency;

                    if (elapsedSeconds > 0.0001)
                    {
                        double newTokens = elapsedSeconds * _refillRate;
                        _tokens = Math.Min((double)_capacity, _tokens + newTokens);
                        _lastRefillTicks = now;
                    }
                }

                public ThrottlerAllocationResult EvaluateTokenRequest(long requestedBytes, long nowNanos = 0)
                {
                    if (requestedBytes <= 0)
                    {
                        return new ThrottlerAllocationResult(0, 0, 0, true);
                    }

                    lock (_syncLock)
                    {
                        if (_isUnlimited || _refillRate <= 0)
                        {
                            return new ThrottlerAllocationResult(requestedBytes, 0, nowNanos, true);
                        }

                        if (nowNanos <= 0)
                        {
                            nowNanos = (long)(Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency));
                        }

                        if (_allocatedUntilNanos < nowNanos)
                        {
                            _allocatedUntilNanos = nowNanos;
                        }

                        bool nativeSuccess = false;
                        ulong takeBytes = 0;
                        ulong waitNanos = 0;
                        ulong newAlloc = 0;

                        ulong maxBurst = (ulong)Math.Max(1, _capacity);
                        ulong waitByteCount = (ulong)Math.Max(1, Math.Min(_capacity / 4, 1024));

                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            try
                            {
                                unsafe
                                {
                                    int status = WinCareCoreNative.WinCareCoreEvaluateRateLimitTokens(
                                        (ulong)nowNanos, (ulong)_allocatedUntilNanos,
                                        (ulong)_refillRate, (ulong)requestedBytes,
                                        waitByteCount, maxBurst,
                                        &takeBytes, &waitNanos, &newAlloc);
                                    if (status == 0)
                                    {
                                        _allocatedUntilNanos = (long)newAlloc;
                                        nativeSuccess = true;
                                    }
                                }
                            }
                            catch
                            {
                                nativeSuccess = false;
                            }
                        }

                        if (!nativeSuccess)
                        {
                            RefillTokens();
                            if (_tokens >= requestedBytes)
                            {
                                _tokens -= requestedBytes;
                                takeBytes = (ulong)requestedBytes;
                                waitNanos = 0;
                            }
                            else
                            {
                                double needed = requestedBytes - _tokens;
                                waitNanos = (ulong)((needed / _refillRate) * 1_000_000_000.0);
                                takeBytes = 0;
                            }
                        }

                        return new ThrottlerAllocationResult(
                            (long)takeBytes,
                            (long)waitNanos,
                            nowNanos + (long)waitNanos,
                            waitNanos == 0 && takeBytes > 0);
                    }
                }
            }

            public sealed class PauseTokenSource
            {
                private volatile TaskCompletionSource<bool>? _pausedTcs;

                public PauseToken Token => new(this);
                public bool IsPaused => _pausedTcs != null;

                public void Pause()
                {
                    Interlocked.CompareExchange(ref _pausedTcs, new TaskCompletionSource<bool>(), null);
                }

                public void Resume()
                {
                    while (true)
                    {
                        var tcs = _pausedTcs;
                        if (tcs == null) return;
                        if (Interlocked.CompareExchange(ref _pausedTcs, null, tcs) == tcs)
                        {
                            tcs.TrySetResult(true);
                            return;
                        }
                    }
                }

                internal Task WaitWhilePausedAsync(CancellationToken cancellationToken = default)
                {
                    var tcs = _pausedTcs;
                    if (tcs == null) return Task.CompletedTask;
                    if (cancellationToken.CanBeCanceled)
                    {
                        return Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, cancellationToken)).Unwrap();
                    }
                    return tcs.Task;
                }
            }

            public readonly struct PauseToken
            {
                private readonly PauseTokenSource? _source;

                public PauseToken(PauseTokenSource source) => _source = source;
                public bool IsPaused => _source?.IsPaused ?? false;
                public Task WaitWhilePausedAsync(CancellationToken cancellationToken = default) =>
                    _source?.WaitWhilePausedAsync(cancellationToken) ?? Task.CompletedTask;
            }

            public static HlsPlaylistInfo ParseHlsPlaylist(string playlistContent, string baseUrl = "")
            {
                if (string.IsNullOrWhiteSpace(playlistContent))
                    return new HlsPlaylistInfo(false, false, 0.0, 0, Array.Empty<HlsVariantInfo>(), Array.Empty<string>());

                string trimmed = playlistContent.TrimStart();
                if (!trimmed.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
                    return new HlsPlaylistInfo(false, false, 0.0, 0, Array.Empty<HlsVariantInfo>(), Array.Empty<string>());

                bool isMaster = playlistContent.Contains("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase);
                var variants = new List<HlsVariantInfo>();
                var segments = new List<string>();
                double targetDuration = 0.0;

                string[] lines = playlistContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

                if (isMaster)
                {
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i].Trim();
                        if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.OrdinalIgnoreCase))
                        {
                            string attrs = line["#EXT-X-STREAM-INF:".Length..];
                            long bandwidth = 0;
                            int? width = null;
                            int? height = null;
                            string? codecs = null;

                            foreach (var part in attrs.Split(','))
                            {
                                var trimmedPart = part.Trim();
                                if (trimmedPart.StartsWith("BANDWIDTH=", StringComparison.OrdinalIgnoreCase))
                                {
                                    long.TryParse(trimmedPart["BANDWIDTH=".Length..].Trim(), out bandwidth);
                                }
                                else if (trimmedPart.StartsWith("RESOLUTION=", StringComparison.OrdinalIgnoreCase))
                                {
                                    var resParts = trimmedPart["RESOLUTION=".Length..].Trim().Split('x');
                                    if (resParts.Length == 2 && int.TryParse(resParts[0], out int w) && int.TryParse(resParts[1], out int h))
                                    {
                                        width = w;
                                        height = h;
                                    }
                                }
                                else if (trimmedPart.StartsWith("CODECS=", StringComparison.OrdinalIgnoreCase))
                                {
                                    codecs = trimmedPart["CODECS=".Length..].Trim('"', '\'');
                                }
                            }

                            for (int j = i + 1; j < lines.Length; j++)
                            {
                                string nextLine = lines[j].Trim();
                                if (!string.IsNullOrEmpty(nextLine) && !nextLine.StartsWith('#'))
                                {
                                    string variantUrl = ResolveUrl(baseUrl, nextLine);
                                    string label = height.HasValue ? $"{height.Value}p" :
                                                   bandwidth > 1_000_000 ? $"{bandwidth / 1_000_000.0:F1} Mbps" :
                                                   bandwidth > 0 ? $"{bandwidth / 1000} kbps" : "Unknown";

                                    variants.Add(new HlsVariantInfo(variantUrl, label, bandwidth, width, height, codecs));
                                    i = j;
                                    break;
                                }
                            }
                        }
                    }

                    variants.Sort((a, b) => b.Bandwidth.CompareTo(a.Bandwidth));
                }
                else
                {
                    foreach (var line in lines)
                    {
                        string l = line.Trim();
                        if (l.StartsWith("#EXT-X-TARGETDURATION:", StringComparison.OrdinalIgnoreCase))
                        {
                            double.TryParse(l["#EXT-X-TARGETDURATION:".Length..].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out targetDuration);
                        }
                        else if (!l.StartsWith('#') && !string.IsNullOrEmpty(l))
                        {
                            segments.Add(ResolveUrl(baseUrl, l));
                        }
                    }
                }

                return new HlsPlaylistInfo(
                    IsValid: true,
                    IsMaster: isMaster,
                    TargetDurationSeconds: targetDuration,
                    SegmentCount: segments.Count,
                    Variants: variants,
                    SegmentUrls: segments);
            }

            private static string ResolveUrl(string baseUrl, string relativeUrl)
            {
                if (string.IsNullOrWhiteSpace(baseUrl) || relativeUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || relativeUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return relativeUrl;

                if (Uri.TryCreate(new Uri(baseUrl), relativeUrl, out Uri? combined))
                    return combined.ToString();

                return relativeUrl;
            }

            public static Ed2kUriInfo ParseEd2kUri(string rawUri)
            {
                if (string.IsNullOrWhiteSpace(rawUri))
                    return new Ed2kUriInfo(false, null, 0, null, "Empty URI");

                string trimmed = rawUri.Trim();
                if (!trimmed.StartsWith("ed2k://|", StringComparison.OrdinalIgnoreCase))
                    return new Ed2kUriInfo(false, null, 0, null, "Not an ed2k scheme");

                string[] parts = trimmed.Split(['|'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 && parts[1].Equals("file", StringComparison.OrdinalIgnoreCase))
                {
                    string rawName = parts[2];
                    string decodedName = Uri.UnescapeDataString(rawName);
                    if (!long.TryParse(parts[3], out long fileBytes) || fileBytes < 0)
                        return new Ed2kUriInfo(false, decodedName, 0, null, "Invalid file size");

                    string hash = parts[4].Trim();
                    if (hash.Length != 32 || !hash.All(c => Uri.IsHexDigit(c)))
                        return new Ed2kUriInfo(false, decodedName, fileBytes, null, "Invalid MD4 hash");

                    return new Ed2kUriInfo(true, decodedName, fileBytes, hash.ToUpperInvariant(), "Valid ed2k file link");
                }

                return new Ed2kUriInfo(false, null, 0, null, "Unsupported ed2k URI structure");
            }

            public static string GenerateUniqueFilename(string targetDirectory, string desiredFilename)
            {
                if (string.IsNullOrWhiteSpace(desiredFilename)) desiredFilename = "download";
                string candidate = SanitizeDownloadFilename(desiredFilename, 200).Sanitized;

                if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
                    return candidate;

                string fullPath = Path.Combine(targetDirectory, candidate);
                if (!File.Exists(fullPath))
                    return candidate;

                string stem = Path.GetFileNameWithoutExtension(candidate);
                string ext = Path.GetExtension(candidate);

                for (int i = 1; i <= 999; i++)
                {
                    string nextName = string.IsNullOrEmpty(ext) ? $"{stem}_{i}" : $"{stem}_{i}{ext}";
                    if (!File.Exists(Path.Combine(targetDirectory, nextName)))
                        return nextName;
                }

                return $"{stem}_{Guid.NewGuid():N}{ext}";
            }

            public static double DecaySearchAffinity(double score, DateTime lastUpdatedUtc, DateTime? nowUtc = null, double halfLifeDays = 90.0)
            {
                if (score <= 0 || double.IsNaN(score) || double.IsInfinity(score)) return 0.0;
                DateTime now = nowUtc ?? DateTime.UtcNow;
                double elapsedDays = Math.Max(0, (now - lastUpdatedUtc).TotalDays);
                return score * Math.Pow(0.5, elapsedDays / Math.Max(1.0, halfLifeDays));
            }

            public static double ApplyPositiveFeedback(double? currentScore, bool wasDirectlyShown, bool fromExternalSource)
            {
                if (!currentScore.HasValue || currentScore.Value <= 0) return 1.0;
                double gain = wasDirectlyShown ? 0.90 : fromExternalSource ? 0.75 : 0.65;
                return Math.Min(4.0, currentScore.Value + gain);
            }

            public static double ApplySkippedFeedback(double currentScore)
            {
                if (currentScore <= 0) return 0.0;
                return Math.Max(0.0, currentScore * 0.82);
            }

            public static string NormalizeSearchQuery(string input)
            {
                if (string.IsNullOrWhiteSpace(input)) return string.Empty;
                string normalized = input.Normalize(System.Text.NormalizationForm.FormKC).Trim().ToLowerInvariant();
                return System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");
            }

            public static bool IsLearnableQuery(string query)
            {
                string compact = NormalizeSearchQuery(query).Replace(" ", "");
                if (compact.Length < 2) return false;
                return compact.Any(char.IsLetterOrDigit);
            }

            public static SteamDebrisReport AuditSteamDebris(string? customSteamappsPath = null)
            {
                var detectedDirs = new List<string>();
                long totalDebrisBytes = 0;
                int shaderCacheCount = 0;
                int downloadingCount = 0;

                var rootsToCheck = new List<string>();
                if (!string.IsNullOrWhiteSpace(customSteamappsPath) && Directory.Exists(customSteamappsPath))
                {
                    rootsToCheck.Add(customSteamappsPath);
                }
                else
                {
                    string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    string defaultSteam = Path.Combine(progFiles, "Steam", "steamapps");
                    if (Directory.Exists(defaultSteam)) rootsToCheck.Add(defaultSteam);

                    foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                    {
                        string altLib = Path.Combine(drive.RootDirectory.FullName, "SteamLibrary", "steamapps");
                        if (Directory.Exists(altLib) && !rootsToCheck.Contains(altLib, StringComparer.OrdinalIgnoreCase))
                        {
                            rootsToCheck.Add(altLib);
                        }
                    }
                }

                foreach (var root in rootsToCheck)
                {
                    detectedDirs.Add(root);

                    string shaderDir = Path.Combine(root, "shadercache");
                    if (Directory.Exists(shaderDir))
                    {
                        var (count, bytes) = CountFilesBounded(shaderDir);
                        shaderCacheCount += count;
                        totalDebrisBytes += bytes;
                    }

                    string downDir = Path.Combine(root, "downloading");
                    if (Directory.Exists(downDir))
                    {
                        var (count, bytes) = CountFilesBounded(downDir);
                        downloadingCount += count;
                        totalDebrisBytes += bytes;
                    }
                }

                return new SteamDebrisReport(
                    LibrariesAudited: detectedDirs,
                    ShaderCacheFileCount: shaderCacheCount,
                    DownloadingFileCount: downloadingCount,
                    TotalDebrisBytes: totalDebrisBytes,
                    Summary: $"Audited {detectedDirs.Count} Steam libraries: {shaderCacheCount} shader cache files, {downloadingCount} incomplete download parts ({totalDebrisBytes / 1024 / 1024} MB total).");
            }

            private static (int count, long bytes) CountFilesBounded(string rootDir, int maxDepth = 4, int maxFiles = 5000)
            {
                int count = 0;
                long bytes = 0;
                var stack = new Stack<(string dir, int depth)>();
                stack.Push((rootDir, 0));

                while (stack.Count > 0 && count < maxFiles)
                {
                    var (currentDir, depth) = stack.Pop();
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(currentDir, "*.*", SearchOption.TopDirectoryOnly))
                        {
                            count++;
                            try
                            {
                                bytes += new FileInfo(file).Length;
                            }
                            catch { }
                            if (count >= maxFiles) break;
                        }

                        if (depth < maxDepth)
                        {
                            foreach (var subDir in Directory.EnumerateDirectories(currentDir, "*", SearchOption.TopDirectoryOnly))
                            {
                                stack.Push((subDir, depth + 1));
                            }
                        }
                    }
                    catch
                    {
                        // Inaccessible directory safely skipped
                    }
                }

                return (count, bytes);
            }

            public sealed class ExponentialMovingAverageSpeedCalculator
            {
                private readonly double _alpha;
                private double _averageSpeedBytesPerSec;
                private bool _isInitialized;

                public double AverageSpeedBytesPerSec => _averageSpeedBytesPerSec;

                public ExponentialMovingAverageSpeedCalculator(double alpha = 0.25)
                {
                    _alpha = Math.Clamp(alpha, 0.01, 1.0);
                }

                public double Update(double instantSpeedBytesPerSec)
                {
                    if (!_isInitialized)
                    {
                        _averageSpeedBytesPerSec = instantSpeedBytesPerSec;
                        _isInitialized = true;
                    }
                    else
                    {
                        _averageSpeedBytesPerSec = (_alpha * instantSpeedBytesPerSec) + ((1.0 - _alpha) * _averageSpeedBytesPerSec);
                    }
                    return _averageSpeedBytesPerSec;
                }

                public TimeSpan CalculateEstimatedRemainingTime(long remainingBytes)
                {
                    if (remainingBytes <= 0) return TimeSpan.Zero;
                    if (_averageSpeedBytesPerSec <= 1.0) return TimeSpan.FromDays(365);
                    double seconds = remainingBytes / _averageSpeedBytesPerSec;
                    if (seconds > 31536000) return TimeSpan.FromDays(365);
                    return TimeSpan.FromSeconds(seconds);
                }
            }

            public static IReadOnlyList<DownloadChunkRange> PartitionByteRanges(long totalBytes, int chunkCount)
            {
                if (totalBytes <= 0 || chunkCount <= 1)
                {
                    return [new DownloadChunkRange(0, 0, Math.Max(0, totalBytes - 1), Math.Max(0, totalBytes))];
                }

                chunkCount = Math.Clamp(chunkCount, 1, 64);
                long chunkSize = totalBytes / chunkCount;
                long remainder = totalBytes % chunkCount;

                var ranges = new List<DownloadChunkRange>(chunkCount);
                long currentStart = 0;

                for (int i = 0; i < chunkCount; i++)
                {
                    long currentSize = chunkSize + (i < remainder ? 1 : 0);
                    long currentEnd = currentStart + currentSize - 1;
                    ranges.Add(new DownloadChunkRange(i, currentStart, currentEnd, currentSize));
                    currentStart = currentEnd + 1;
                }

                return ranges;
            }

            public static class AdbDiagnosticsHelper
            {
                public sealed record AdbDeviceItem(
                    string Serial,
                    string State,
                    string Product,
                    string Model,
                    string Device,
                    string TransportId);

                public sealed record AdbStorageItem(
                    string Filesystem,
                    string TotalSize,
                    string Used,
                    string Available,
                    string UsePercent,
                    string MountedOn);

                public sealed record AdbBatteryReport(
                    int Level,
                    int Scale,
                    int Health,
                    int TemperatureC,
                    int VoltageMv,
                    bool AcPowered,
                    bool UsbPowered,
                    string Summary);

                public static IReadOnlyList<AdbDeviceItem> ParseAdbDevices(string adbOutput)
                {
                    var list = new List<AdbDeviceItem>();
                    if (string.IsNullOrWhiteSpace(adbOutput)) return list;

                    var lines = adbOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("*", StringComparison.Ordinal))
                            continue;

                        var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 2) continue;

                        string serial = parts[0];
                        string state = parts[1];
                        string product = "";
                        string model = "";
                        string device = "";
                        string transportId = "";

                        for (int i = 2; i < parts.Length; i++)
                        {
                            var kv = parts[i].Split(':');
                            if (kv.Length == 2)
                            {
                                switch (kv[0].ToLowerInvariant())
                                {
                                    case "product": product = kv[1]; break;
                                    case "model": model = kv[1]; break;
                                    case "device": device = kv[1]; break;
                                    case "transport_id": transportId = kv[1]; break;
                                }
                            }
                        }

                        list.Add(new AdbDeviceItem(serial, state, product, model, device, transportId));
                    }

                    return list;
                }

                public static IReadOnlyList<AdbStorageItem> ParseAdbStorageInfo(string dfOutput)
                {
                    var list = new List<AdbStorageItem>();
                    if (string.IsNullOrWhiteSpace(dfOutput)) return list;

                    var lines = dfOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("Filesystem", StringComparison.OrdinalIgnoreCase)) continue;

                        var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 6)
                        {
                            list.Add(new AdbStorageItem(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5]));
                        }
                    }

                    return list;
                }

                public static AdbBatteryReport ParseAdbBatteryInfo(string dumpsysBatteryOutput)
                {
                    int level = 0, scale = 100, health = 1, temp = 0, voltage = 0;
                    bool ac = false, usb = false;

                    if (!string.IsNullOrWhiteSpace(dumpsysBatteryOutput))
                    {
                        var lines = dumpsysBatteryOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            var parts = line.Trim().Split(':');
                            if (parts.Length != 2) continue;
                            string key = parts[0].Trim().ToLowerInvariant();
                            string val = parts[1].Trim();

                            switch (key)
                            {
                                case "level": int.TryParse(val, out level); break;
                                case "scale": int.TryParse(val, out scale); break;
                                case "health": int.TryParse(val, out health); break;
                                case "temperature":
                                    if (int.TryParse(val, out int rawT)) temp = rawT / 10;
                                    break;
                                case "voltage": int.TryParse(val, out voltage); break;
                                case "ac powered": ac = val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                                case "usb powered": usb = val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                            }
                        }
                    }

                    int pct = (scale > 0) ? (level * 100) / scale : level;
                    string summary = $"Battery: {pct}% · Health: {health} · Temp: {temp}°C · Voltage: {voltage}mV · Power: {(ac ? "AC" : usb ? "USB" : "Battery")}";
                    return new AdbBatteryReport(pct, scale, health, temp, voltage, ac, usb, summary);
                }
            }

            public static class SynthesizedInputFilter
            {
                public static bool IsSynthesizedKeystroke(uint flags)
                {
                    return (flags & WindowsInterop.LfInjected) != 0 || (flags & WindowsInterop.LfLowerIlInjected) != 0;
                }

                public static bool IsOemSpecificKey(int virtualKey)
                {
                    return (virtualKey >= 0x92 && virtualKey <= 0x96)
                        || virtualKey == 0xE1
                        || (virtualKey >= 0xE3 && virtualKey <= 0xE4)
                        || virtualKey == 0xE6
                        || (virtualKey >= 0xE9 && virtualKey <= 0xF5);
                }
            }

            public static class PowerGovernorCalculator
            {
                public static double CalculateAdaptiveTdp(
                    double cpuTempC,
                    double minTdpWatts = 10.0,
                    double targetTdpWatts = 25.0,
                    double maxTdpWatts = 35.0,
                    double thermalThrottleTempC = 85.0,
                    bool isOnAcPower = true)
                {
                    if (!isOnAcPower)
                    {
                        maxTdpWatts = Math.Min(maxTdpWatts, targetTdpWatts);
                    }

                    if (cpuTempC >= thermalThrottleTempC)
                    {
                        double excess = cpuTempC - thermalThrottleTempC;
                        double throttled = targetTdpWatts - (excess * 1.5);
                        return Math.Clamp(throttled, minTdpWatts, maxTdpWatts);
                    }

                    if (cpuTempC < 65.0 && isOnAcPower)
                    {
                        return maxTdpWatts;
                    }

                    return targetTdpWatts;
                }
            }

            public static class VectorSearchHelper
            {
                public static unsafe float CosineSimilarity(IReadOnlyList<float> vecA, IReadOnlyList<float> vecB)
                {
                    if (vecA == null || vecB == null || vecA.Count == 0 || vecA.Count != vecB.Count)
                        return 0.0f;

                    int len = vecA.Count;
                    fixed (float* pA = vecA.ToArray())
                    fixed (float* pB = vecB.ToArray())
                    {
                        float nativeSim = 0.0f;
                        int status = WinCare.Infrastructure.Native.WinCareCoreNative.WinCareCoreVectorCosineSimilarity(pA, pB, (nuint)len, &nativeSim);
                        if (status == 0)
                        {
                            return nativeSim;
                        }
                    }

                    double dot = 0.0, normA = 0.0, normB = 0.0;
                    for (int i = 0; i < len; i++)
                    {
                        double a = vecA[i];
                        double b = vecB[i];
                        dot += a * b;
                        normA += a * a;
                        normB += b * b;
                    }
                    double denom = Math.Sqrt(normA) * Math.Sqrt(normB);
                    return (denom < 1e-12) ? 0.0f : (float)Math.Clamp(dot / denom, -1.0, 1.0);
                }

                public static IReadOnlyList<(string Role, string Content)> TruncatePromptContext(
                    IReadOnlyList<(string Role, string Content)> messages,
                    int maxTokens,
                    Func<string, int> tokenEstimator)
                {
                    if (messages == null || messages.Count == 0) return Array.Empty<(string, string)>();
                    if (maxTokens <= 0) return Array.Empty<(string, string)>();

                    var systemMessages = messages.Where(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase)).ToList();
                    int systemTokens = systemMessages.Sum(m => tokenEstimator(m.Content));

                    int budgetForChat = Math.Max(0, maxTokens - systemTokens);
                    var nonSystem = messages.Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase)).ToList();

                    var retainedChat = new List<(string Role, string Content)>();
                    int currentTokens = 0;

                    for (int i = nonSystem.Count - 1; i >= 0; i--)
                    {
                        int tokens = tokenEstimator(nonSystem[i].Content);
                        if (currentTokens + tokens <= budgetForChat)
                        {
                            retainedChat.Add(nonSystem[i]);
                            currentTokens += tokens;
                        }
                        else
                        {
                            break;
                        }
                    }

                    retainedChat.Reverse();
                    var result = new List<(string Role, string Content)>(systemMessages.Count + retainedChat.Count);
                    result.AddRange(systemMessages);
                    result.AddRange(retainedChat);
                    return result;
                }
            }

            public sealed class PieceMapBitset
            {
                private const int PiecesPerByte = 8;
                private readonly byte[] _data;
                private int _completedPieces;

                public int PieceCount { get; }
                public long PieceSize { get; }
                public int CompletedPieces => _completedPieces;
                public bool IsComplete => _completedPieces == PieceCount;
                public double ProgressPercentage => PieceCount == 0 ? 100.0 : (double)_completedPieces / PieceCount * 100.0;
                public ReadOnlySpan<byte> Data => _data;

                public PieceMapBitset(int pieceCount, long pieceSize)
                {
                    PieceCount = Math.Max(0, pieceCount);
                    PieceSize = Math.Max(1, pieceSize);
                    int byteCount = (PieceCount + PiecesPerByte - 1) / PiecesPerByte;
                    _data = new byte[byteCount];
                    _completedPieces = 0;
                }

                public PieceMapBitset(int pieceCount, long pieceSize, byte[] data)
                {
                    PieceCount = Math.Max(0, pieceCount);
                    PieceSize = Math.Max(1, pieceSize);
                    int expectedBytes = (PieceCount + PiecesPerByte - 1) / PiecesPerByte;
                    if (data == null || data.Length != expectedBytes)
                    {
                        throw new ArgumentException($"Invalid piece map byte count: expected {expectedBytes}, got {data?.Length ?? 0}");
                    }
                    _data = (byte[])data.Clone();
                    ClearUnusedBits();
                    RecalculatePopcount();
                }

                public bool IsCompleted(int index)
                {
                    if (index < 0 || index >= PieceCount) return false;
                    int byteIdx = index / PiecesPerByte;
                    int bitIdx = index % PiecesPerByte;
                    return (_data[byteIdx] & (1 << bitIdx)) != 0;
                }

                public bool SetCompleted(int index, bool completed)
                {
                    if (index < 0 || index >= PieceCount) return false;
                    int byteIdx = index / PiecesPerByte;
                    int bitIdx = index % PiecesPerByte;
                    bool current = (_data[byteIdx] & (1 << bitIdx)) != 0;
                    if (current == completed) return current;

                    if (completed)
                    {
                        _data[byteIdx] |= (byte)(1 << bitIdx);
                        _completedPieces++;
                    }
                    else
                    {
                        _data[byteIdx] &= (byte)~(1 << bitIdx);
                        _completedPieces--;
                    }
                    return current;
                }

                public IReadOnlyList<int> GetMissingPieceIndices()
                {
                    var missing = new List<int>(PieceCount - _completedPieces);
                    for (int i = 0; i < PieceCount; i++)
                    {
                        if (!IsCompleted(i))
                        {
                            missing.Add(i);
                        }
                    }
                    return missing;
                }

                public IReadOnlyList<(int StartIndex, int Count)> GetMissingRanges()
                {
                    var ranges = new List<(int StartIndex, int Count)>();
                    int currentStart = -1;
                    int currentCount = 0;

                    for (int i = 0; i < PieceCount; i++)
                    {
                        if (!IsCompleted(i))
                        {
                            if (currentStart == -1)
                            {
                                currentStart = i;
                                currentCount = 1;
                            }
                            else
                            {
                                currentCount++;
                            }
                        }
                        else
                        {
                            if (currentStart != -1)
                            {
                                ranges.Add((currentStart, currentCount));
                                currentStart = -1;
                                currentCount = 0;
                            }
                        }
                    }

                    if (currentStart != -1)
                    {
                        ranges.Add((currentStart, currentCount));
                    }

                    return ranges;
                }

                public string ToBase64() => Convert.ToBase64String(_data);

                public static PieceMapBitset FromBase64(string base64, int pieceCount, long pieceSize)
                {
                    byte[] bytes = Convert.FromBase64String(base64);
                    return new PieceMapBitset(pieceCount, pieceSize, bytes);
                }

                private void ClearUnusedBits()
                {
                    int remainder = PieceCount % PiecesPerByte;
                    if (remainder != 0 && _data.Length > 0)
                    {
                        byte mask = (byte)((1 << remainder) - 1);
                        _data[^1] &= mask;
                    }
                }

                private void RecalculatePopcount()
                {
                    if (_data.Length == 0)
                    {
                        _completedPieces = 0;
                        return;
                    }

                    bool nativeSuccess = false;
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        try
                        {
                            unsafe
                            {
                                fixed (byte* pData = _data)
                                {
                                    ulong count = 0;
                                    int status = WinCareCoreNative.WinCareCorePieceMapPopcount(pData, (nuint)_data.Length, &count);
                                    if (status == 0)
                                    {
                                        _completedPieces = (int)count;
                                        nativeSuccess = true;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            nativeSuccess = false;
                        }
                    }

                    if (!nativeSuccess)
                    {
                        int sum = 0;
                        foreach (byte b in _data)
                        {
                            sum += System.Numerics.BitOperations.PopCount(b);
                        }
                        _completedPieces = sum;
                    }
                }
            }

            public sealed record ScheduleTimesConfig(
                IReadOnlySet<DayOfWeek> DaysOfWeek,
                TimeOnly StartTime,
                TimeOnly EndTime,
                bool EnabledStartTime,
                bool EnabledEndTime);

            public static class ScheduleTimesCalculator
            {
                public static DateTime GetNearestTimeToStart(ScheduleTimesConfig config, DateTime now)
                {
                    return GetNearestTime(config.DaysOfWeek, config.StartTime, now);
                }

                public static DateTime GetNearestTimeToStop(ScheduleTimesConfig config, DateTime now)
                {
                    return GetNearestTime(config.DaysOfWeek, config.EndTime, now);
                }

                public static bool IsWithinScheduledWindow(ScheduleTimesConfig config, DateTime now)
                {
                    if (!config.DaysOfWeek.Contains(now.DayOfWeek)) return false;
                    TimeOnly time = TimeOnly.FromDateTime(now);

                    if (config.StartTime <= config.EndTime)
                    {
                        return time >= config.StartTime && time <= config.EndTime;
                    }
                    else
                    {
                        return time >= config.StartTime || time <= config.EndTime;
                    }
                }

                private static DateTime GetNearestTime(IReadOnlySet<DayOfWeek> days, TimeOnly targetTime, DateTime now)
                {
                    if (days == null || days.Count == 0)
                    {
                        days = new HashSet<DayOfWeek>((DayOfWeek[])Enum.GetValues(typeof(DayOfWeek)));
                    }

                    TimeOnly currentTime = TimeOnly.FromDateTime(now);
                    DayOfWeek currentDay = now.DayOfWeek;

                    for (int dayOffset = 0; dayOffset <= 7; dayOffset++)
                    {
                        DayOfWeek candidateDay = (DayOfWeek)(((int)currentDay + dayOffset) % 7);
                        if (days.Contains(candidateDay))
                        {
                            if (dayOffset == 0)
                            {
                                if (currentTime < targetTime)
                                {
                                    return now.Date.Add(targetTime.ToTimeSpan());
                                }
                            }
                            else
                            {
                                return now.Date.AddDays(dayOffset).Add(targetTime.ToTimeSpan());
                            }
                        }
                    }

                    return now.Date.AddDays(7).Add(targetTime.ToTimeSpan());
                }
            }

            public static class SearchAffinityEngine
            {
                public const long SearchAffinityHalfLifeMs = 90L * 24 * 60 * 60 * 1000;
                public const double SearchAffinityMinScore = 0.35;
                public const double SearchAffinityMaxScore = 4.0;
                public const double SearchAffinitySkipFactor = 0.82;

                public static string NormalizeSearchQuery(string input)
                {
                    if (string.IsNullOrWhiteSpace(input)) return string.Empty;

                    string normalized = input.Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant();
                    var condensed = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");
                    return condensed.Length > 160 ? condensed[..160] : condensed;
                }

                public static bool IsLearnableSearchQuery(string query)
                {
                    string compact = NormalizeSearchQuery(query).Replace(" ", "");
                    if (string.IsNullOrEmpty(compact)) return false;

                    int letterOrDigitCount = compact.Count(char.IsLetterOrDigit);
                    return letterOrDigitCount >= 2;
                }

                public static double DecayAffinityScore(double score, long elapsedMs, long halfLifeMs = SearchAffinityHalfLifeMs)
                {
                    if (double.IsNaN(score) || double.IsInfinity(score) || score <= 0 || halfLifeMs <= 0)
                        return 0.0;

                    elapsedMs = Math.Max(0, elapsedMs);
                    bool nativeSuccess = false;
                    double result = 0.0;

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        try
                        {
                            unsafe
                            {
                                double outScore = 0;
                                int status = WinCareCoreNative.WinCareCoreDecayAffinityScore(
                                    score, (ulong)elapsedMs, (ulong)halfLifeMs, &outScore);
                                if (status == 0)
                                {
                                    result = outScore;
                                    nativeSuccess = true;
                                }
                            }
                        }
                        catch
                        {
                            nativeSuccess = false;
                        }
                    }

                    if (!nativeSuccess)
                    {
                        double exponent = -(double)elapsedMs / halfLifeMs;
                        result = score * Math.Pow(2.0, exponent);
                    }

                    return Math.Clamp(result, 0.0, SearchAffinityMaxScore);
                }

                public static double ApplyPositiveFeedback(double? currentScore, bool wasShown, bool fromExternal)
                {
                    if (currentScore == null || currentScore.Value <= 0) return 1.0;
                    double gain = wasShown ? 0.9 : fromExternal ? 0.75 : 0.65;
                    return Math.Min(SearchAffinityMaxScore, currentScore.Value + gain);
                }

                public static double ApplySkippedFeedback(double currentScore)
                {
                    if (currentScore <= 0) return 0.0;
                    return Math.Max(0.0, currentScore * SearchAffinitySkipFactor);
                }

                public static double ComputeLearnedRank(double score)
                {
                    return 6000.0 + Math.Min(SearchAffinityMaxScore, Math.Max(0.0, score)) * 100.0;
                }
            }

            public sealed class SystemExecutionGovernor : IDisposable
            {
                private bool _disposed;

                public bool IsActive => !_disposed;

                public SystemExecutionGovernor(bool preventDisplaySleep = false)
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        uint flags = WindowsInterop.EsContinuous | WindowsInterop.EsSystemRequired | WindowsInterop.EsAwaymodeRequired;
                        if (preventDisplaySleep)
                        {
                            flags |= WindowsInterop.EsDisplayRequired;
                        }
                        WindowsInterop.SetThreadExecutionState(flags);
                    }
                }

                public void Dispose()
                {
                    if (!_disposed)
                    {
                        _disposed = true;
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            WindowsInterop.SetThreadExecutionState(WindowsInterop.EsContinuous);
                        }
                    }
                }
            }

            public sealed class LayerStack<TKey, TAction> where TKey : notnull
            {
                private readonly Dictionary<string, IReadOnlyDictionary<TKey, TAction>> _layers = new(StringComparer.OrdinalIgnoreCase);
                private readonly List<string> _stack = [];

                public IReadOnlyList<string> ActiveStack => _stack;

                public void DefineLayer(string layerName, IReadOnlyDictionary<TKey, TAction> mapping)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(layerName);
                    ArgumentNullException.ThrowIfNull(mapping);
                    _layers[layerName] = mapping;
                }

                public void PushLayer(string layerName)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(layerName);
                    if (!_layers.ContainsKey(layerName))
                    {
                        throw new KeyNotFoundException($"Layer '{layerName}' is not defined.");
                    }
                    _stack.Remove(layerName);
                    _stack.Insert(0, layerName);
                }

                public bool PopLayer(string layerName)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(layerName);
                    return _stack.Remove(layerName);
                }

                public bool TryResolveKey(TKey key, out TAction? action)
                {
                    action = default;
                    foreach (string layer in _stack)
                    {
                        if (_layers.TryGetValue(layer, out var map) && map.TryGetValue(key, out var val))
                        {
                            action = val;
                            return true;
                        }
                    }
                    return false;
                }
            }
        }

    public enum DownloadFileCategory
    {
        Compressed,
        Programs,
        Videos,
        Music,
        Pictures,
        Documents,
        Other
    }

    public sealed record DownloadCategoryClassification(
        DownloadFileCategory Category,
        string Extension,
        string SubfolderName);

    public sealed record DownloadChunkRange(
        int Index,
        long StartByte,
        long EndByte,
        long ChunkSize);

    public sealed record MultiPartArchiveInfo(
        bool IsMultiPart,
        string BaseName,
        int PartNumber,
        bool IsFirstPart);

    public sealed record ThrottlerAllocationResult(
        long GrantedBytes,
        long WaitNanos,
        long NewAllocatedUntilNanos,
        bool CanProceedImmediately);

    public sealed record ResourceHealthReport(
        int CheckedCount,
        int ExistingCount,
        int MissingCount,
        IReadOnlyList<string> MissingPaths);

    public sealed record RelocationMatch(
        string OriginalPath,
        string RelocatedPath,
        long FileSizeBytes);

    public sealed record StaleNodeModulesCandidate(
        string Path,
        string ProjectRoot,
        DateTime LastWriteTimeUtc,
        bool IsStale);

    public sealed record ToolchainCacheCandidate(
        string Ecosystem,
        string Path,
        bool Exists);

    public sealed record DownloadFilenameSanitizationResult(
        string Original,
        string Sanitized,
        bool WasModified,
        bool DosStemDetected);

    public sealed record DownloadFreeSpaceResult(
        string DriveName,
        long AvailableBytes,
        long RequiredBytes,
        long SafetyBufferBytes,
        bool IsSufficient,
        string Summary);

    public sealed record BandwidthEstimate(
        double InstantaneousBps,
        double SmoothedBps,
        long EtaSeconds,
        bool HasEta);

    public sealed record ContentRangeHeaderInfo(
        bool IsValid,
        long StartByte,
        long EndByte,
        long TotalBytes,
        string StatusMessage);

    public sealed record SteamGameInspectionResult(
        string AppId,
        string GameName,
        string InstallDirectory,
        string ManifestPath,
        string? CoverImagePath);

    public sealed record PathSizeResult(
        long TotalBytes,
        int EntriesCount,
        bool IsTruncated,
        string Summary);

    public enum ModelFitClassification
    {
        FitsVramFully,
        PartialOffloadGpu,
        CpuRamOnly,
        InsufficientMemory
    }

    public sealed record ModelMemoryFitResult(
        long ParameterCount,
        int QuantizationBits,
        long WeightBytes,
        long KvCacheBytes,
        long ActivationBytes,
        long TotalRequiredBytes,
        ModelFitClassification Classification,
        string Explanation);

    public sealed record TrackerSanitizationResult(
        IReadOnlyList<string> UniqueTrackers,
        string CommaSeparated,
        string LineSeparated,
        int DroppedCount);

    public sealed record ServerRangeSupportInfo(
        bool SupportsByteRanges,
        bool IsIndeterminateLength,
        long TotalBytes,
        string RangeMode);

    public sealed record HlsVariantInfo(
        string Url,
        string Label,
        long Bandwidth,
        int? Width,
        int? Height,
        string? Codecs);

    public sealed record HlsPlaylistInfo(
        bool IsValid,
        bool IsMaster,
        double TargetDurationSeconds,
        int SegmentCount,
        IReadOnlyList<HlsVariantInfo> Variants,
        IReadOnlyList<string> SegmentUrls);

    public sealed record Ed2kUriInfo(
        bool IsValid,
        string? FileName,
        long FileSizeBytes,
        string? Md4HashHex,
        string StatusMessage);

    public sealed record SteamDebrisReport(
        IReadOnlyList<string> LibrariesAudited,
        int ShaderCacheFileCount,
        int DownloadingFileCount,
        long TotalDebrisBytes,
        string Summary);

    public CommandHandlerOutcome AuditDeveloperJunk(CommandParameters p)
    {
        int maxDepth = p.Int32("MaxDepth", 4, 1, 8);
        int olderThanDays = p.Int32("OlderThanDays", 30, 1, 365);

        IReadOnlyList<StaleNodeModulesCandidate> candidates = DeveloperJunkHelper.ScanNodeModules(null, maxDepth, olderThanDays);
        IReadOnlyList<ToolchainCacheCandidate> toolchainCaches = DeveloperJunkHelper.ScanToolchainCaches();

        return Success("developer-junk-audit", $"Found {candidates.Count} node_modules trees ({candidates.Count(c => c.IsStale)} stale) and {toolchainCaches.Count(c => c.Exists)} toolchain caches.", new
        {
            totalFound = candidates.Count,
            staleCount = candidates.Count(c => c.IsStale),
            candidates,
            toolchainCaches,
        });
    }
}
