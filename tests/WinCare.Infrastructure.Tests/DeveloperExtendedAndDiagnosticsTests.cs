using WinCare.Infrastructure.Commands;

namespace WinCare.Infrastructure.Tests;

public sealed class DeveloperExtendedAndDiagnosticsTests : IDisposable
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), "wincare-dev-test-" + Guid.NewGuid().ToString("N"));

    public DeveloperExtendedAndDiagnosticsTests()
    {
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); } catch { }
    }

    [Fact]
    public void ScanAdbCaches_returns_non_null_list()
    {
        var caches = WindowsCommandExecutor.DeveloperJunkHelper.ScanAdbCaches();
        Assert.NotNull(caches);
    }

    [Fact]
    public void ScanRedisCaches_detects_redis_snapshots_in_specified_roots()
    {
        string projectRoot = Path.Combine(_testDir, "my-redis-service");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "dump.rdb"), "REDIS0009fakecontent");
        File.WriteAllText(Path.Combine(projectRoot, "appendonly.aof"), "FAKE AOF CONTENT");

        var caches = WindowsCommandExecutor.DeveloperJunkHelper.ScanRedisCaches([_testDir, projectRoot]);
        Assert.Contains(caches, path => path.EndsWith("dump.rdb", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(caches, path => path.EndsWith("appendonly.aof", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScanIncompleteDownloads_runs_without_exceptions()
    {
        var incomplete = WindowsCommandExecutor.DeveloperJunkHelper.ScanIncompleteDownloads(olderThanDays: 0);
        Assert.NotNull(incomplete);
    }

    [Fact]
    public void ValidateConfigurationStructure_handles_json_yaml_and_depth_limits()
    {
        // Valid JSON
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.ValidateConfigurationStructure("""{"key": "value", "list": [1, 2, 3]}""", out string? error));
        Assert.Null(error);

        // Empty content fails
        Assert.False(WindowsCommandExecutor.DeveloperJunkHelper.ValidateConfigurationStructure("", out error));
        Assert.NotNull(error);

        // Valid YAML-like indentation
        string validYaml = """
        server:
          port: 8080
          name: WinCareGateway
        """;
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.ValidateConfigurationStructure(validYaml, out error));
        Assert.Null(error);

        // Odd invalid indentation
        string invalidIndent = """
        server:
           port: 8080
        """;
        Assert.False(WindowsCommandExecutor.DeveloperJunkHelper.ValidateConfigurationStructure(invalidIndent, out error));
        Assert.NotNull(error);
    }

    [Fact]
    public void ScanDiagnosticTools_discovers_windows_diagnostics()
    {
        var tools = WindowsCommandExecutor.DiagnosticUtilityHelper.ScanDiagnosticTools();
        Assert.NotEmpty(tools);
        Assert.Contains(tools, t => t.ExeName.Equals("cleanmgr.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tools, t => t.ExeName.Equals("resmon.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tools, t => t.ExeName.Equals("perfmon.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SanitizeDownloadFilename_handles_dos_stems_and_illegal_characters()
    {
        var resCon = WindowsCommandExecutor.DeveloperJunkHelper.SanitizeDownloadFilename("CON.txt");
        Assert.True(resCon.DosStemDetected);
        Assert.Equal("_CON.txt", resCon.Sanitized);
        Assert.True(resCon.WasModified);

        var resIllegal = WindowsCommandExecutor.DeveloperJunkHelper.SanitizeDownloadFilename("file*name?:<>.zip");
        Assert.False(resIllegal.DosStemDetected);
        Assert.Contains('_', resIllegal.Sanitized);
        Assert.DoesNotContain('*', resIllegal.Sanitized);
        Assert.DoesNotContain('?', resIllegal.Sanitized);
        Assert.DoesNotContain(':', resIllegal.Sanitized);
        Assert.DoesNotContain('<', resIllegal.Sanitized);
        Assert.DoesNotContain('>', resIllegal.Sanitized);

        var resClean = WindowsCommandExecutor.DeveloperJunkHelper.SanitizeDownloadFilename("archive-v2.5.0.tar.gz");
        Assert.False(resClean.DosStemDetected);
        Assert.Equal("archive-v2.5.0.tar.gz", resClean.Sanitized);

        string veryLongName = new string('a', 200) + ".part";
        var resLong = WindowsCommandExecutor.DeveloperJunkHelper.SanitizeDownloadFilename(veryLongName, maxLength: 50);
        Assert.True(resLong.Sanitized.Length <= 50);
        Assert.EndsWith(".part", resLong.Sanitized);
    }

    [Fact]
    public void ValidateDownloadFreeSpace_evaluates_system_drive()
    {
        var spaceResult = WindowsCommandExecutor.DeveloperJunkHelper.ValidateDownloadFreeSpace(@"C:\Users", requiredBytes: 1024);
        Assert.NotNull(spaceResult);
        Assert.False(string.IsNullOrWhiteSpace(spaceResult.DriveName));
        Assert.False(string.IsNullOrWhiteSpace(spaceResult.Summary));
        Assert.True(spaceResult.AvailableBytes > 0);
    }

    [Fact]
    public void IsRetryableDownloadStatus_correctly_classifies_http_status_codes()
    {
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.IsRetryableDownloadStatus(408));
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.IsRetryableDownloadStatus(429));
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.IsRetryableDownloadStatus(500));
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.IsRetryableDownloadStatus(502));
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.IsRetryableDownloadStatus(503));
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.IsRetryableDownloadStatus(504));

        Assert.False(WindowsCommandExecutor.DeveloperJunkHelper.IsRetryableDownloadStatus(200));
        Assert.False(WindowsCommandExecutor.DeveloperJunkHelper.IsRetryableDownloadStatus(404));
        Assert.False(WindowsCommandExecutor.DeveloperJunkHelper.IsRetryableDownloadStatus(401));
        Assert.False(WindowsCommandExecutor.DeveloperJunkHelper.IsRetryableDownloadStatus(403));
    }

    [Fact]
    public void CategorizeDownloadFile_classifies_all_standard_types()
    {
        var compressed = WindowsCommandExecutor.DeveloperJunkHelper.CategorizeDownloadFile("archive.zip");
        Assert.Equal(WindowsCommandExecutor.DownloadFileCategory.Compressed, compressed.Category);
        Assert.Equal("Compressed", compressed.SubfolderName);

        var program = WindowsCommandExecutor.DeveloperJunkHelper.CategorizeDownloadFile("installer.msi");
        Assert.Equal(WindowsCommandExecutor.DownloadFileCategory.Programs, program.Category);

        var video = WindowsCommandExecutor.DeveloperJunkHelper.CategorizeDownloadFile("recording.mp4");
        Assert.Equal(WindowsCommandExecutor.DownloadFileCategory.Videos, video.Category);

        var music = WindowsCommandExecutor.DeveloperJunkHelper.CategorizeDownloadFile("track.flac");
        Assert.Equal(WindowsCommandExecutor.DownloadFileCategory.Music, music.Category);

        var picture = WindowsCommandExecutor.DeveloperJunkHelper.CategorizeDownloadFile("avatar.png");
        Assert.Equal(WindowsCommandExecutor.DownloadFileCategory.Pictures, picture.Category);

        var doc = WindowsCommandExecutor.DeveloperJunkHelper.CategorizeDownloadFile("paper.pdf");
        Assert.Equal(WindowsCommandExecutor.DownloadFileCategory.Documents, doc.Category);

        var other = WindowsCommandExecutor.DeveloperJunkHelper.CategorizeDownloadFile("data.unknownext");
        Assert.Equal(WindowsCommandExecutor.DownloadFileCategory.Other, other.Category);
    }

    [Fact]
    public void CalculateDownloadChunks_partitions_ranges_without_overlap_or_gaps()
    {
        const long totalSize = 10_000_007L;
        const int threads = 4;

        var chunks = WindowsCommandExecutor.DeveloperJunkHelper.CalculateDownloadChunks(totalSize, threads);

        Assert.Equal(threads, chunks.Count);
        Assert.Equal(0, chunks[0].StartByte);
        Assert.Equal(totalSize - 1, chunks[threads - 1].EndByte);

        long sumBytes = 0;
        for (int i = 0; i < chunks.Count; i++)
        {
            sumBytes += chunks[i].ChunkSize;
            if (i > 0)
            {
                Assert.Equal(chunks[i - 1].EndByte + 1, chunks[i].StartByte);
            }
        }

        Assert.Equal(totalSize, sumBytes);
    }

    [Fact]
    public void DetectMultiPartArchiveGroup_identifies_multipart_patterns()
    {
        var part1Rar = WindowsCommandExecutor.DeveloperJunkHelper.DetectMultiPartArchiveGroup("release.part01.rar");
        Assert.True(part1Rar.IsMultiPart);
        Assert.Equal("release", part1Rar.BaseName);
        Assert.Equal(1, part1Rar.PartNumber);
        Assert.True(part1Rar.IsFirstPart);

        var part2Rar = WindowsCommandExecutor.DeveloperJunkHelper.DetectMultiPartArchiveGroup("release.part02.rar");
        Assert.True(part2Rar.IsMultiPart);
        Assert.Equal("release", part2Rar.BaseName);
        Assert.Equal(2, part2Rar.PartNumber);
        Assert.False(part2Rar.IsFirstPart);

        var part7z = WindowsCommandExecutor.DeveloperJunkHelper.DetectMultiPartArchiveGroup("dataset.7z.001");
        Assert.True(part7z.IsMultiPart);
        Assert.Equal("dataset.7z", part7z.BaseName);
        Assert.Equal(1, part7z.PartNumber);
        Assert.True(part7z.IsFirstPart);

        var singleZip = WindowsCommandExecutor.DeveloperJunkHelper.DetectMultiPartArchiveGroup("single.zip");
        Assert.False(singleZip.IsMultiPart);
    }

    [Fact]
    public void TokenBucketThrottlerCalculator_evaluates_immediate_and_wait_durations()
    {
        // 1. Unlimited (bytesPerSecond <= 0)
        var unlimited = WindowsCommandExecutor.DeveloperJunkHelper.TokenBucketThrottlerCalculator.CalculateAllocation(
            nowNanos: 1_000_000,
            allocatedUntilNanos: 1_000_000,
            requestedBytes: 50_000,
            bytesPerSecond: 0);

        Assert.True(unlimited.CanProceedImmediately);
        Assert.Equal(50_000, unlimited.GrantedBytes);
        Assert.Equal(0, unlimited.WaitNanos);

        // 2. Throttled within burst limit (1 MB/s, request 32 KB)
        const long oneMbPerSec = 1024 * 1024;
        var immediate = WindowsCommandExecutor.DeveloperJunkHelper.TokenBucketThrottlerCalculator.CalculateAllocation(
            nowNanos: 1_000_000,
            allocatedUntilNanos: 1_000_000,
            requestedBytes: 32 * 1024,
            bytesPerSecond: oneMbPerSec);

        Assert.True(immediate.CanProceedImmediately);
        Assert.Equal(32 * 1024, immediate.GrantedBytes);
        Assert.True(immediate.NewAllocatedUntilNanos > 1_000_000);
    }

    [Fact]
    public void IsSignedUrl_detects_cloud_signatures()
    {
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.IsSignedUrl("https://mybucket.s3.amazonaws.com/image.png?X-Amz-Expires=86400&X-Amz-Date=20260913"));
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.IsSignedUrl("https://mystorage.blob.core.windows.net/vhd/disk.vhd?se=2026-09-13T12%3A00%3A00Z&sp=r"));
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.IsSignedUrl("https://media.example.com/stream.m3u8?expires=1726224000"));
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.IsSignedUrl("https://storage.googleapis.com/obj/file.tar?x-goog-signature=abcdef1234"));

        Assert.False(WindowsCommandExecutor.DeveloperJunkHelper.IsSignedUrl("https://example.com/downloads/setup.exe"));
        Assert.False(WindowsCommandExecutor.DeveloperJunkHelper.IsSignedUrl(null));
    }

    [Fact]
    public void IsSameVolume_and_CheckLocalResourceHealth()
    {
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.IsSameVolume(@"C:\Windows\System32", @"C:\Users\Default"));
        Assert.False(WindowsCommandExecutor.DeveloperJunkHelper.IsSameVolume(@"C:\Windows", @"D:\Backups"));

        var health = WindowsCommandExecutor.DeveloperJunkHelper.CheckLocalResourceHealth(new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            @"C:\NonExistent_Fake_Directory_98765"
        });

        Assert.Equal(2, health.CheckedCount);
        Assert.Equal(1, health.ExistingCount);
        Assert.Equal(1, health.MissingCount);
        Assert.Single(health.MissingPaths);
    }

    [Fact]
    public void CalculateRollingBandwidth_computes_smoothed_rate_and_eta()
    {
        // Initial transfer
        var first = WindowsCommandExecutor.DeveloperJunkHelper.CalculateRollingBandwidth(
            totalBytesTransferred: 1_000_000,
            previousBytesTransferred: 0,
            elapsedSeconds: 1.0,
            previousBps: 0.0,
            smoothingFactorAlpha: 0.25,
            totalFileBytes: 10_000_000);

        Assert.Equal(1_000_000.0, first.InstantaneousBps);
        Assert.Equal(1_000_000.0, first.SmoothedBps);
        Assert.True(first.HasEta);
        Assert.Equal(9, first.EtaSeconds); // 9MB remaining at 1MB/s

        // Second transfer with speed drop to 500 KB/s
        var second = WindowsCommandExecutor.DeveloperJunkHelper.CalculateRollingBandwidth(
            totalBytesTransferred: 1_500_000,
            previousBytesTransferred: 1_000_000,
            elapsedSeconds: 1.0,
            previousBps: first.SmoothedBps,
            smoothingFactorAlpha: 0.5,
            totalFileBytes: 10_000_000);

        Assert.Equal(500_000.0, second.InstantaneousBps);
        Assert.Equal(750_000.0, second.SmoothedBps); // (0.5 * 500k) + (0.5 * 1M)
        Assert.True(second.HasEta);
    }

    [Fact]
    public void ParseContentRangeHeader_parses_ranges_and_totals()
    {
        var valid = WindowsCommandExecutor.DeveloperJunkHelper.ParseContentRangeHeader("bytes 0-1023/2048");
        Assert.True(valid.IsValid);
        Assert.Equal(0, valid.StartByte);
        Assert.Equal(1023, valid.EndByte);
        Assert.Equal(2048, valid.TotalBytes);

        var wildcard = WindowsCommandExecutor.DeveloperJunkHelper.ParseContentRangeHeader("bytes */5000");
        Assert.True(wildcard.IsValid);
        Assert.Equal(5000, wildcard.TotalBytes);

        var invalidUnit = WindowsCommandExecutor.DeveloperJunkHelper.ParseContentRangeHeader("items 0-10/20");
        Assert.False(invalidUnit.IsValid);

        var missing = WindowsCommandExecutor.DeveloperJunkHelper.ParseContentRangeHeader(null);
        Assert.False(missing.IsValid);
    }

    [Fact]
    public void IsHelperOrInstallerBinary_identifies_installers_and_updaters()
    {
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.IsHelperOrInstallerBinary(@"C:\Program Files\App\uninstall.exe"));
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.IsHelperOrInstallerBinary(@"C:\Temp\Setup.exe"));
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.IsHelperOrInstallerBinary(@"C:\App\crashpad_handler.exe"));
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.IsHelperOrInstallerBinary(@"C:\App\Update.exe"));
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.IsHelperOrInstallerBinary(@"C:\App\vcredist_x64.exe"));

        Assert.False(WindowsCommandExecutor.DeveloperJunkHelper.IsHelperOrInstallerBinary(@"C:\Program Files\App\WinCare.exe"));
        Assert.False(WindowsCommandExecutor.DeveloperJunkHelper.IsHelperOrInstallerBinary(@"C:\Program Files\Notepad++\notepad++.exe"));
        Assert.False(WindowsCommandExecutor.DeveloperJunkHelper.IsHelperOrInstallerBinary(@"C:\App\document.pdf"));
    }

    [Fact]
    public void DetectSteamGameInstall_finds_game_metadata_and_covers()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "WinCare_SteamMock_" + Guid.NewGuid().ToString("N"));
        string libraryCache = Path.Combine(tempDir, "librarycache");
        Directory.CreateDirectory(libraryCache);

        try
        {
            string acfContent = @"
""AppState""
{
    ""appid""       ""1091500""
    ""name""        ""Cyberpunk 2077""
    ""installdir""   ""Cyberpunk 2077""
}
";
            File.WriteAllText(Path.Combine(tempDir, "appmanifest_1091500.acf"), acfContent);
            string mockCover = Path.Combine(libraryCache, "1091500_header.jpg");
            File.WriteAllText(mockCover, "fake image");

            var result = WindowsCommandExecutor.DeveloperJunkHelper.DetectSteamGameInstall(tempDir, "Cyberpunk 2077");
            Assert.NotNull(result);
            Assert.Equal("1091500", result.AppId);
            Assert.Equal("Cyberpunk 2077", result.GameName);
            Assert.Equal(mockCover, result.CoverImagePath);

            var notFound = WindowsCommandExecutor.DeveloperJunkHelper.DetectSteamGameInstall(tempDir, "Half-Life 3");
            Assert.Null(notFound);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void ComputeAffinityDecay_and_Feedback_behave_mathematically()
    {
        const long ninetyDaysMs = 90L * 24 * 60 * 60 * 1000;

        // At 0 elapsed, decay is 100% (unchanged)
        double score0 = WindowsCommandExecutor.DeveloperJunkHelper.ComputeAffinityDecay(2.0, 0, ninetyDaysMs);
        Assert.Equal(2.0, score0, 3);

        // At exactly one half life, score halves
        double scoreHalf = WindowsCommandExecutor.DeveloperJunkHelper.ComputeAffinityDecay(2.0, ninetyDaysMs, ninetyDaysMs);
        Assert.Equal(1.0, scoreHalf, 3);

        // At two half lives, score quarters
        double scoreQuarter = WindowsCommandExecutor.DeveloperJunkHelper.ComputeAffinityDecay(2.0, ninetyDaysMs * 2, ninetyDaysMs);
        Assert.Equal(0.5, scoreQuarter, 3);

        // Feedback boosts with ceiling
        double feedback1 = WindowsCommandExecutor.DeveloperJunkHelper.ApplyAffinityFeedback(1.0, wasDirectlyInteracted: true, maxCeiling: 4.0);
        Assert.Equal(1.9, feedback1, 3);

        double capped = WindowsCommandExecutor.DeveloperJunkHelper.ApplyAffinityFeedback(3.8, wasDirectlyInteracted: true, maxCeiling: 4.0);
        Assert.Equal(4.0, capped, 3);
    }

    [Fact]
    public void SparseFile_and_Volume_support()
    {
        string systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        bool supportsSparse = WindowsCommandExecutor.DeveloperJunkHelper.CanVolumeSupportSparseFiles(systemDrive);
        Assert.True(supportsSparse);

        Assert.False(WindowsCommandExecutor.DeveloperJunkHelper.SetSparseFile(null!));
        Assert.False(WindowsCommandExecutor.DeveloperJunkHelper.SetSparseFile(@"C:\NonExistent_Sparse_File_12345.bin"));
    }

    [Fact]
    public void GetPathSizeBounded_evaluates_with_bounds()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "WinCare_PathSize_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            File.WriteAllText(Path.Combine(tempDir, "file1.txt"), "hello world");
            File.WriteAllText(Path.Combine(tempDir, "file2.txt"), "antigravity");

            var result = WindowsCommandExecutor.DeveloperJunkHelper.GetPathSizeBounded(tempDir, maxEntries: 100, maxDurationMs: 2000);
            Assert.False(result.IsTruncated);
            Assert.True(result.TotalBytes > 0);
            Assert.True(result.EntriesCount >= 2);

            var nonExistent = WindowsCommandExecutor.DeveloperJunkHelper.GetPathSizeBounded(@"C:\NonExistent_Dir_98765");
            Assert.Equal(0, nonExistent.TotalBytes);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void GetDeduplicatedFilePath_numbers_existing_files()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "WinCare_Dedup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string target = Path.Combine(tempDir, "download.zip");
            File.WriteAllText(target, "content");

            string dedup1 = WindowsCommandExecutor.DeveloperJunkHelper.GetDeduplicatedFilePath(target);
            Assert.Equal(Path.Combine(tempDir, "download_1.zip"), dedup1);

            File.WriteAllText(dedup1, "content2");
            string dedup2 = WindowsCommandExecutor.DeveloperJunkHelper.GetDeduplicatedFilePath(target);
            Assert.Equal(Path.Combine(tempDir, "download_2.zip"), dedup2);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void FormatFileUri_handles_local_and_unc_paths()
    {
        Assert.Equal("file:///C:/Users/ACER/file.txt", WindowsCommandExecutor.DeveloperJunkHelper.FormatFileUri(@"C:\Users\ACER\file.txt"));
        Assert.Equal("file:////server/share/file.iso", WindowsCommandExecutor.DeveloperJunkHelper.FormatFileUri(@"\\server\share\file.iso"));
        Assert.Equal(string.Empty, WindowsCommandExecutor.DeveloperJunkHelper.FormatFileUri(null!));
    }

    [Fact]
    public void TokenBucketRateLimiter_controls_throughput_and_handles_unlimited()
    {
        // 1. Unlimited
        var unlimited = WindowsCommandExecutor.DeveloperJunkHelper.TokenBucketRateLimiter.Unlimited();
        Assert.True(unlimited.IsUnlimited);
        Assert.True(unlimited.TryAcquire(1_000_000));
        Assert.Equal(1_000_000, unlimited.TransferredBytes);

        // 2. Limited (1000 bytes/sec)
        var limiter = new WindowsCommandExecutor.DeveloperJunkHelper.TokenBucketRateLimiter(1000);
        Assert.False(limiter.IsUnlimited);
        Assert.Equal(1000, limiter.Capacity);
        Assert.Equal(1000, limiter.RefillRate);

        // First 500 bytes should acquire immediately
        Assert.True(limiter.TryAcquire(500));
        // Next 500 bytes should acquire immediately
        Assert.True(limiter.TryAcquire(500));
        // Next 500 bytes should fail (bucket empty)
        Assert.False(limiter.TryAcquire(500));

        // Dynamic rate change to unlimited
        limiter.SetRateLimit(0);
        Assert.True(limiter.IsUnlimited);
        Assert.True(limiter.TryAcquire(500));
    }

    [Fact]
    public void SanitizeTrackerList_cleans_deduplicates_and_truncates()
    {
        var raw = new[]
        {
            "# OpenTrackers list",
            "udp://tracker.opentrackr.org:1337/announce",
            "  ",
            "http://tracker.openbittorrent.com:80/announce",
            "udp://tracker.opentrackr.org:1337/announce", // duplicate
            "ftp://invalid-tracker.org/announce", // invalid scheme
            "https://tracker.example.com/announce,udp://tracker2.example.com:6969/announce" // comma-separated
        };

        var result = WindowsCommandExecutor.DeveloperJunkHelper.SanitizeTrackerList(raw, maxBufferLength: 2048);

        Assert.Equal(4, result.UniqueTrackers.Count);
        Assert.True(result.DroppedCount >= 2);
        Assert.Contains("udp://tracker.opentrackr.org:1337/announce", result.UniqueTrackers);
        Assert.Contains("http://tracker.openbittorrent.com:80/announce", result.UniqueTrackers);
        Assert.Contains("https://tracker.example.com/announce", result.UniqueTrackers);
        Assert.Contains("udp://tracker2.example.com:6969/announce", result.UniqueTrackers);
        Assert.False(string.IsNullOrEmpty(result.CommaSeparated));
        Assert.False(string.IsNullOrEmpty(result.LineSeparated));
    }

    [Fact]
    public void ExtractDownloadUrls_finds_magnet_and_direct_downloads()
    {
        string text = @"
            Check out these links:
            magnet:?xt=urn:btih:c12fe1c06bba254a9dc9f519b335de7ec72523ad&dn=Ubuntu
            And direct files:
            https://download.example.com/builds/wincare-setup.exe?utm_source=twitter&utm_medium=social
            https://models.example.org/v1/llama-3-8b-instruct.Q4_K_M.gguf
            Ignore normal web pages like https://github.com/WinCare/WinCare
        ";

        var urls = WindowsCommandExecutor.DeveloperJunkHelper.ExtractDownloadUrls(text);

        Assert.Equal(3, urls.Count);
        Assert.Contains(urls, u => u.StartsWith("magnet:?xt=urn:btih:"));
        Assert.Contains(urls, u => u.Contains("wincare-setup.exe") && !u.Contains("utm_source"));
        Assert.Contains(urls, u => u.EndsWith("llama-3-8b-instruct.Q4_K_M.gguf"));
    }

    [Fact]
    public void EstimateModelMemoryFit_calculates_correct_classifications()
    {
        // 1. 3B Q4 model on 24GB VRAM -> FitsVramFully
        var fit1 = WindowsCommandExecutor.DeveloperJunkHelper.EstimateModelMemoryFit(
            parameterCount: 3_000_000_000,
            quantizationBits: 4,
            contextTokens: 2048,
            availableVramBytes: 24_000_000_000,
            availableRamBytes: 32_000_000_000);

        Assert.Equal(WindowsCommandExecutor.ModelFitClassification.FitsVramFully, fit1.Classification);
        Assert.True(fit1.WeightBytes > 0);
        Assert.True(fit1.TotalRequiredBytes < 24_000_000_000);

        // 2. 8B Q8 model on 10GB VRAM with 64k context -> PartialOffloadGpu
        var fit2 = WindowsCommandExecutor.DeveloperJunkHelper.EstimateModelMemoryFit(
            parameterCount: 8_000_000_000,
            quantizationBits: 8,
            contextTokens: 65536,
            availableVramBytes: 10_000_000_000,
            availableRamBytes: 64_000_000_000);

        Assert.Equal(WindowsCommandExecutor.ModelFitClassification.PartialOffloadGpu, fit2.Classification);

        // 3. 14B Q4 model with 0 VRAM on 64GB RAM -> CpuRamOnly
        var fit3 = WindowsCommandExecutor.DeveloperJunkHelper.EstimateModelMemoryFit(
            parameterCount: 14_000_000_000,
            quantizationBits: 4,
            contextTokens: 4096,
            availableVramBytes: 0,
            availableRamBytes: 64_000_000_000);

        Assert.Equal(WindowsCommandExecutor.ModelFitClassification.CpuRamOnly, fit3.Classification);

        // 4. 70B FP16 model on 8GB RAM -> InsufficientMemory
        var fit4 = WindowsCommandExecutor.DeveloperJunkHelper.EstimateModelMemoryFit(
            parameterCount: 70_000_000_000,
            quantizationBits: 16,
            contextTokens: 8192,
            availableVramBytes: 0,
            availableRamBytes: 8_000_000_000);

        Assert.Equal(WindowsCommandExecutor.ModelFitClassification.InsufficientMemory, fit4.Classification);
    }

    [Fact]
    public void ValidateServerRangeSupport_detects_bytes_and_none()
    {
        var ranges1 = WindowsCommandExecutor.DeveloperJunkHelper.ValidateServerRangeSupport("bytes", null, 1048576);
        Assert.True(ranges1.SupportsByteRanges);
        Assert.False(ranges1.IsIndeterminateLength);
        Assert.Equal(1048576, ranges1.TotalBytes);

        var ranges2 = WindowsCommandExecutor.DeveloperJunkHelper.ValidateServerRangeSupport("none", null, 5000);
        Assert.False(ranges2.SupportsByteRanges);
        Assert.Equal("SingleStreamOnly_NonResumable", ranges2.RangeMode);

        var ranges3 = WindowsCommandExecutor.DeveloperJunkHelper.ValidateServerRangeSupport(null, "bytes 0-1023/2048", null);
        Assert.True(ranges3.SupportsByteRanges);
        Assert.Equal(2048, ranges3.TotalBytes);
    }

    [Fact]
    public void ParseHlsPlaylist_master_and_media_variants()
    {
        string masterContent = """
            #EXTM3U
            #EXT-X-STREAM-INF:BANDWIDTH=1280000,RESOLUTION=1280x720,CODECS="avc1.4d401f,mp4a.40.2"
            720p.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=2560000,RESOLUTION=1920x1080,CODECS="avc1.640028,mp4a.40.2"
            1080p.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=640000,RESOLUTION=640x360
            360p.m3u8
            """;

        var master = WindowsCommandExecutor.DeveloperJunkHelper.ParseHlsPlaylist(masterContent, "https://example.com/hls/master.m3u8");
        Assert.True(master.IsValid);
        Assert.True(master.IsMaster);
        Assert.Equal(3, master.Variants.Count);
        Assert.Equal("1080p", master.Variants[0].Label);
        Assert.Equal(2560000, master.Variants[0].Bandwidth);
        Assert.Equal("https://example.com/hls/1080p.m3u8", master.Variants[0].Url);

        string mediaContent = """
            #EXTM3U
            #EXT-X-TARGETDURATION:10
            #EXT-X-VERSION:3
            #EXTINF:9.009,
            segment001.ts
            #EXTINF:9.009,
            segment002.ts
            #EXTINF:3.003,
            segment003.ts
            #EXT-X-ENDLIST
            """;

        var media = WindowsCommandExecutor.DeveloperJunkHelper.ParseHlsPlaylist(mediaContent, "https://example.com/hls/720p.m3u8");
        Assert.True(media.IsValid);
        Assert.False(media.IsMaster);
        Assert.Equal(10.0, media.TargetDurationSeconds);
        Assert.Equal(3, media.SegmentCount);
        Assert.Equal("https://example.com/hls/segment001.ts", media.SegmentUrls[0]);
    }

    [Fact]
    public void ParseEd2kUri_valid_and_invalid_inputs()
    {
        string validUri = "ed2k://|file|Ubuntu_Desktop_x64.iso|4630972416|8867C5E54405FF9452225B66EFEE690A|/";
        var valid = WindowsCommandExecutor.DeveloperJunkHelper.ParseEd2kUri(validUri);
        Assert.True(valid.IsValid);
        Assert.Equal("Ubuntu_Desktop_x64.iso", valid.FileName);
        Assert.Equal(4630972416, valid.FileSizeBytes);
        Assert.Equal("8867C5E54405FF9452225B66EFEE690A", valid.Md4HashHex);

        var invalidShort = WindowsCommandExecutor.DeveloperJunkHelper.ParseEd2kUri("ed2k://|file|short.txt|100|badhash|/");
        Assert.False(invalidShort.IsValid);

        var invalidScheme = WindowsCommandExecutor.DeveloperJunkHelper.ParseEd2kUri("https://example.com/file.iso");
        Assert.False(invalidScheme.IsValid);
    }

    [Fact]
    public void GenerateUniqueFilename_avoids_collisions()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "wincare_unique_fn_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. When file does not exist, returns original name
            string name1 = WindowsCommandExecutor.DeveloperJunkHelper.GenerateUniqueFilename(tempDir, "document.pdf");
            Assert.Equal("document.pdf", name1);

            // 2. Create the file, next call should produce document_1.pdf
            File.WriteAllText(Path.Combine(tempDir, "document.pdf"), "hello");
            string name2 = WindowsCommandExecutor.DeveloperJunkHelper.GenerateUniqueFilename(tempDir, "document.pdf");
            Assert.Equal("document_1.pdf", name2);

            // 3. Create document_1.pdf, next call should produce document_2.pdf
            File.WriteAllText(Path.Combine(tempDir, "document_1.pdf"), "hello");
            string name3 = WindowsCommandExecutor.DeveloperJunkHelper.GenerateUniqueFilename(tempDir, "document.pdf");
            Assert.Equal("document_2.pdf", name3);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SearchAffinity_decay_positive_and_skipped_feedback()
    {
        DateTime baseDate = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // 1. Decay with 90-day half-life: after 90 days, 2.0 should decay to ~1.0
        DateTime day90 = baseDate.AddDays(90);
        double decayed = WindowsCommandExecutor.DeveloperJunkHelper.DecaySearchAffinity(2.0, baseDate, day90, 90.0);
        Assert.InRange(decayed, 0.99, 1.01);

        // 2. Positive feedback on new query initializes to 1.0
        double initial = WindowsCommandExecutor.DeveloperJunkHelper.ApplyPositiveFeedback(null, false, false);
        Assert.Equal(1.0, initial);

        // 3. Positive feedback directly shown adds +0.90
        double reinforced = WindowsCommandExecutor.DeveloperJunkHelper.ApplyPositiveFeedback(1.0, wasDirectlyShown: true, fromExternalSource: false);
        Assert.Equal(1.90, reinforced);

        // 4. Skipped feedback applies 0.82 factor
        double skipped = WindowsCommandExecutor.DeveloperJunkHelper.ApplySkippedFeedback(2.0);
        Assert.Equal(1.64, skipped);
    }

    [Fact]
    public void NormalizeSearchQuery_and_IsLearnableQuery()
    {
        Assert.Equal("disk clean", WindowsCommandExecutor.DeveloperJunkHelper.NormalizeSearchQuery("  DISK   CLEAN  "));
        Assert.Equal("system status", WindowsCommandExecutor.DeveloperJunkHelper.NormalizeSearchQuery("System  Status\t"));

        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.IsLearnableQuery("disk"));
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.IsLearnableQuery("内存清理"));
        Assert.False(WindowsCommandExecutor.DeveloperJunkHelper.IsLearnableQuery("!"));
        Assert.False(WindowsCommandExecutor.DeveloperJunkHelper.IsLearnableQuery(""));
    }

    [Fact]
    public void AuditSteamDebris_executes_safely()
    {
        var debris = WindowsCommandExecutor.DeveloperJunkHelper.AuditSteamDebris(customSteamappsPath: null);
        Assert.NotNull(debris);
        Assert.NotNull(debris.LibrariesAudited);
        Assert.True(debris.ShaderCacheFileCount >= 0);
        Assert.True(debris.TotalDebrisBytes >= 0);
        Assert.False(string.IsNullOrWhiteSpace(debris.Summary));
    }

    [Fact]
    public async Task PauseTokenSource_cooperative_pause_and_resume()
    {
        var pts = new WindowsCommandExecutor.DeveloperJunkHelper.PauseTokenSource();
        var token = pts.Token;

        Assert.False(token.IsPaused);

        // Awaiting while not paused should complete immediately
        var waitTask = token.WaitWhilePausedAsync();
        Assert.True(waitTask.IsCompleted);

        // Pause
        pts.Pause();
        Assert.True(token.IsPaused);

        // Awaiting while paused should remain pending until resumed
        var pausedWait = token.WaitWhilePausedAsync();
        Assert.False(pausedWait.IsCompleted);

        pts.Resume();
        Assert.False(token.IsPaused);
        await pausedWait;
        Assert.True(pausedWait.IsCompleted);
    }

    [Fact]
    public void ExponentialMovingAverageSpeedCalculator_updates_and_computes_eta()
    {
        var ema = new WindowsCommandExecutor.DeveloperJunkHelper.ExponentialMovingAverageSpeedCalculator(alpha: 0.5);
        Assert.Equal(0.0, ema.AverageSpeedBytesPerSec);

        // First update initializes value
        double speed1 = ema.Update(1000.0);
        Assert.Equal(1000.0, speed1);

        // Second update: 0.5 * 2000 + 0.5 * 1000 = 1500
        double speed2 = ema.Update(2000.0);
        Assert.Equal(1500.0, speed2);

        // Remaining time: 3000 bytes at 1500 bytes/sec = 2 seconds
        var eta = ema.CalculateEstimatedRemainingTime(3000);
        Assert.Equal(2, (int)eta.TotalSeconds);

        var zeroEta = ema.CalculateEstimatedRemainingTime(0);
        Assert.Equal(TimeSpan.Zero, zeroEta);
    }

    [Fact]
    public void PartitionByteRanges_partitions_correctly()
    {
        // 1000 bytes into 4 chunks: 250 bytes each
        var ranges4 = WindowsCommandExecutor.DeveloperJunkHelper.PartitionByteRanges(1000, 4);
        Assert.Equal(4, ranges4.Count);
        Assert.Equal(0, ranges4[0].StartByte);
        Assert.Equal(249, ranges4[0].EndByte);
        Assert.Equal(250, ranges4[0].ChunkSize);

        Assert.Equal(250, ranges4[1].StartByte);
        Assert.Equal(499, ranges4[1].EndByte);

        Assert.Equal(750, ranges4[3].StartByte);
        Assert.Equal(999, ranges4[3].EndByte);

        // 10 bytes into 3 chunks: 4, 3, 3 bytes
        var ranges3 = WindowsCommandExecutor.DeveloperJunkHelper.PartitionByteRanges(10, 3);
        Assert.Equal(3, ranges3.Count);
        Assert.Equal(4, ranges3[0].ChunkSize);
        Assert.Equal(3, ranges3[1].ChunkSize);
        Assert.Equal(3, ranges3[2].ChunkSize);
    }

    [Fact]
    public void AdbDiagnosticsHelper_parses_devices_storage_and_battery()
    {
        // Devices
        string devicesOutput = "List of devices attached\r\nRF8M10XXXXX\tdevice product:star2qltezc model:SM_G9650 device:star2qlte transport_id:1\r\n";
        var devices = WindowsCommandExecutor.DeveloperJunkHelper.AdbDiagnosticsHelper.ParseAdbDevices(devicesOutput);
        Assert.Single(devices);
        Assert.Equal("RF8M10XXXXX", devices[0].Serial);
        Assert.Equal("device", devices[0].State);
        Assert.Equal("SM_G9650", devices[0].Model);
        Assert.Equal("1", devices[0].TransportId);

        // Storage
        string dfOutput = "Filesystem     1K-blocks      Used Available Use% Mounted on\r\n/dev/block/dm-0 119842528 45678900  74163628  39% /data\r\n";
        var storage = WindowsCommandExecutor.DeveloperJunkHelper.AdbDiagnosticsHelper.ParseAdbStorageInfo(dfOutput);
        Assert.Single(storage);
        Assert.Equal("/dev/block/dm-0", storage[0].Filesystem);
        Assert.Equal("39%", storage[0].UsePercent);
        Assert.Equal("/data", storage[0].MountedOn);

        // Battery
        string batteryOutput = "Current Battery Service state:\r\n  AC powered: true\r\n  USB powered: false\r\n  level: 85\r\n  scale: 100\r\n  voltage: 4150\r\n  temperature: 280\r\n  health: 2\r\n";
        var battery = WindowsCommandExecutor.DeveloperJunkHelper.AdbDiagnosticsHelper.ParseAdbBatteryInfo(batteryOutput);
        Assert.Equal(85, battery.Level);
        Assert.Equal(28, battery.TemperatureC);
        Assert.Equal(4150, battery.VoltageMv);
        Assert.True(battery.AcPowered);
        Assert.False(battery.UsbPowered);
        Assert.Contains("85%", battery.Summary);
    }

    [Fact]
    public void SynthesizedInputFilter_identifies_injected_and_oem_keys()
    {
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.SynthesizedInputFilter.IsSynthesizedKeystroke(0x01)); // LLKHF_INJECTED
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.SynthesizedInputFilter.IsSynthesizedKeystroke(0x02)); // LLKHF_LOWER_IL_INJECTED
        Assert.False(WindowsCommandExecutor.DeveloperJunkHelper.SynthesizedInputFilter.IsSynthesizedKeystroke(0x00));

        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.SynthesizedInputFilter.IsOemSpecificKey(0x92));
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.SynthesizedInputFilter.IsOemSpecificKey(0xE1));
        Assert.False(WindowsCommandExecutor.DeveloperJunkHelper.SynthesizedInputFilter.IsOemSpecificKey(0x41)); // 'A'
    }

    [Fact]
    public void PowerGovernorCalculator_calculates_tdp_correctly()
    {
        // Normal cold temp on AC power -> Max TDP (35W)
        double coldAc = WindowsCommandExecutor.DeveloperJunkHelper.PowerGovernorCalculator.CalculateAdaptiveTdp(50.0, 10.0, 25.0, 35.0, 85.0, true);
        Assert.Equal(35.0, coldAc);

        // Cold temp on battery power -> Target TDP (25W)
        double coldBat = WindowsCommandExecutor.DeveloperJunkHelper.PowerGovernorCalculator.CalculateAdaptiveTdp(50.0, 10.0, 25.0, 35.0, 85.0, false);
        Assert.Equal(25.0, coldBat);

        // Overheating temp (90C >= 85C threshold) -> throttles below target (25 - 5*1.5 = 17.5W)
        double overheated = WindowsCommandExecutor.DeveloperJunkHelper.PowerGovernorCalculator.CalculateAdaptiveTdp(90.0, 10.0, 25.0, 35.0, 85.0, true);
        Assert.Equal(17.5, overheated);
    }

    [Fact]
    public void VectorSearchHelper_computes_cosine_similarity_and_truncates_context()
    {
        float[] a = [1.0f, 0.0f, 0.0f];
        float[] b = [1.0f, 0.0f, 0.0f];
        float[] c = [0.0f, 1.0f, 0.0f];

        float simSame = WindowsCommandExecutor.DeveloperJunkHelper.VectorSearchHelper.CosineSimilarity(a, b);
        Assert.Equal(1.0f, simSame, 3);

        float simOrth = WindowsCommandExecutor.DeveloperJunkHelper.VectorSearchHelper.CosineSimilarity(a, c);
        Assert.Equal(0.0f, simOrth, 3);

        // Context truncation
        var messages = new List<(string Role, string Content)>
        {
            ("system", "You are WinCare AI."),
            ("user", "Old query 1"),
            ("assistant", "Old reply 1"),
            ("user", "Recent query 2"),
            ("assistant", "Recent reply 2")
        };

        // Budget of 10 words: should preserve system prompt + most recent turns
        var truncated = WindowsCommandExecutor.DeveloperJunkHelper.VectorSearchHelper.TruncatePromptContext(
            messages,
            maxTokens: 12,
            tokenEstimator: s => s.Split(' ').Length);

        Assert.True(truncated.Count >= 2);
        Assert.Equal("system", truncated[0].Role);
        Assert.Equal("assistant", truncated[^1].Role);
    }

    [Fact]
    public void PieceMapBitset_tracks_bits_popcount_and_missing_ranges()
    {
        var bitset = new WindowsCommandExecutor.DeveloperJunkHelper.PieceMapBitset(64, 1024);
        Assert.Equal(64, bitset.PieceCount);
        Assert.Equal(0, bitset.CompletedPieces);
        Assert.Equal(0.0, bitset.ProgressPercentage);

        bitset.SetCompleted(0, true);
        bitset.SetCompleted(10, true);
        bitset.SetCompleted(63, true);

        Assert.True(bitset.IsCompleted(0));
        Assert.True(bitset.IsCompleted(10));
        Assert.True(bitset.IsCompleted(63));
        Assert.False(bitset.IsCompleted(1));
        Assert.False(bitset.IsCompleted(11));

        Assert.Equal(3, bitset.CompletedPieces);
        Assert.Equal(3.0 / 64.0 * 100.0, bitset.ProgressPercentage, 2);

        // Serialization round-trip
        string b64 = bitset.ToBase64();
        var restored = WindowsCommandExecutor.DeveloperJunkHelper.PieceMapBitset.FromBase64(b64, 64, 1024);
        Assert.Equal(3, restored.CompletedPieces);
        Assert.True(restored.IsCompleted(0));
        Assert.True(restored.IsCompleted(10));
        Assert.True(restored.IsCompleted(63));

        // Missing ranges
        var missing = bitset.GetMissingRanges();
        Assert.NotEmpty(missing);
        Assert.Equal(1, missing[0].StartIndex);
        Assert.Equal(9, missing[0].Count);

        // Clear bit
        bitset.SetCompleted(10, false);
        Assert.False(bitset.IsCompleted(10));
        Assert.Equal(2, bitset.CompletedPieces);
    }

    [Fact]
    public void TokenBucketRateLimiter_EvaluateTokenRequest_evaluates_burst_and_wait()
    {
        var limiter = new WindowsCommandExecutor.DeveloperJunkHelper.TokenBucketRateLimiter(1000L);

        // Immediate small request should have waitNanos == 0
        var evalSmall = limiter.EvaluateTokenRequest(requestedBytes: 500, nowNanos: 1_000_000_000L);
        Assert.Equal(500, evalSmall.GrantedBytes);
        Assert.Equal(0, evalSmall.WaitNanos);

        // Second request at same timestamp: fulfills remaining burst
        var evalBurst = limiter.EvaluateTokenRequest(requestedBytes: 5000, nowNanos: 1_000_000_000L);
        Assert.True(evalBurst.GrantedBytes > 0);

        // Third request at same timestamp: burst is now exhausted, so must wait
        var evalWait = limiter.EvaluateTokenRequest(requestedBytes: 5000, nowNanos: 1_000_000_000L);
        Assert.Equal(0, evalWait.GrantedBytes);
        Assert.True(evalWait.WaitNanos > 0);
    }

    [Fact]
    public void ScheduleTimesCalculator_computes_next_active_window()
    {
        // Active Monday, Wednesday, Friday from 02:00 to 06:00
        var activeDays = new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday };
        var startTime = new TimeOnly(2, 0);
        var stopTime = new TimeOnly(6, 0);
        var config = new WindowsCommandExecutor.DeveloperJunkHelper.ScheduleTimesConfig(activeDays, startTime, stopTime, true, true);

        // Reference time: Sunday 2026-09-13 23:00:00 UTC (DayOfWeek.Sunday)
        var refSunday = new DateTime(2026, 9, 13, 23, 0, 0, DateTimeKind.Utc);
        var nextStart = WindowsCommandExecutor.DeveloperJunkHelper.ScheduleTimesCalculator.GetNearestTimeToStart(config, refSunday);
        var nextStop = WindowsCommandExecutor.DeveloperJunkHelper.ScheduleTimesCalculator.GetNearestTimeToStop(config, refSunday);

        Assert.Equal(DayOfWeek.Monday, nextStart.DayOfWeek);
        Assert.Equal(2, nextStart.Hour);
        Assert.Equal(DayOfWeek.Monday, nextStop.DayOfWeek);
        Assert.Equal(6, nextStop.Hour);
        Assert.True(nextStop > nextStart);

        // Reference time: Monday 2026-09-14 03:30:00 UTC (inside the window)
        var refMondayInWindow = new DateTime(2026, 9, 14, 3, 30, 0, DateTimeKind.Utc);
        bool inWindow = WindowsCommandExecutor.DeveloperJunkHelper.ScheduleTimesCalculator.IsWithinScheduledWindow(config, refMondayInWindow);
        Assert.True(inWindow);
    }

    [Fact]
    public void SearchAffinityEngine_records_decays_and_ranks()
    {
        // Normalization FormKC
        string norm = WindowsCommandExecutor.DeveloperJunkHelper.SearchAffinityEngine.NormalizeSearchQuery("  Clean   TEMP   Files!  ");
        Assert.Equal("clean temp files!", norm);
        Assert.True(WindowsCommandExecutor.DeveloperJunkHelper.SearchAffinityEngine.IsLearnableSearchQuery("clean"));
        Assert.False(WindowsCommandExecutor.DeveloperJunkHelper.SearchAffinityEngine.IsLearnableSearchQuery("a"));

        // Feedback
        double scoreInitial = WindowsCommandExecutor.DeveloperJunkHelper.SearchAffinityEngine.ApplyPositiveFeedback(null, wasShown: true, fromExternal: false);
        Assert.Equal(1.0, scoreInitial);

        double scoreBoosted = WindowsCommandExecutor.DeveloperJunkHelper.SearchAffinityEngine.ApplyPositiveFeedback(scoreInitial, wasShown: true, fromExternal: false);
        Assert.True(scoreBoosted > scoreInitial);

        // Decay calculation (C ABI + fallback)
        long halfLife = 24L * 60 * 60 * 1000;
        double scoreDecayed = WindowsCommandExecutor.DeveloperJunkHelper.SearchAffinityEngine.DecayAffinityScore(scoreBoosted, elapsedMs: halfLife, halfLifeMs: halfLife);
        Assert.True(scoreDecayed < scoreBoosted);
        Assert.True(scoreDecayed > 0.0);

        // Skip penalty
        double scoreSkipped = WindowsCommandExecutor.DeveloperJunkHelper.SearchAffinityEngine.ApplySkippedFeedback(scoreBoosted);
        Assert.True(scoreSkipped < scoreBoosted);

        // Learned rank
        double rank = WindowsCommandExecutor.DeveloperJunkHelper.SearchAffinityEngine.ComputeLearnedRank(scoreBoosted);
        Assert.True(rank >= 6000.0);
    }

    [Fact]
    public void SystemExecutionGovernor_disposes_cleanly()
    {
        using (var governor = new WindowsCommandExecutor.DeveloperJunkHelper.SystemExecutionGovernor())
        {
            Assert.True(governor.IsActive);
        }
    }

    [Fact]
    public void LayerStack_manages_overlays_and_fallthrough()
    {
        var stack = new WindowsCommandExecutor.DeveloperJunkHelper.LayerStack<string, string>();
        Assert.Empty(stack.ActiveStack);

        var baseLayer = new Dictionary<string, string>
        {
            ["key_a"] = "action_a",
            ["key_b"] = "action_b",
            ["key_c"] = "action_c"
        };
        stack.DefineLayer("base", baseLayer);
        stack.PushLayer("base");
        Assert.Single(stack.ActiveStack);

        Assert.True(stack.TryResolveKey("key_a", out string? actA));
        Assert.Equal("action_a", actA);
        Assert.True(stack.TryResolveKey("key_b", out string? actB));
        Assert.Equal("action_b", actB);

        // Overlay layer overriding key_b
        var fnLayer = new Dictionary<string, string>
        {
            ["key_b"] = "action_b_fn_override",
            ["key_d"] = "action_d_fn_only"
        };
        stack.DefineLayer("fn", fnLayer);
        stack.PushLayer("fn");
        Assert.Equal(2, stack.ActiveStack.Count);

        // key_b should be the override
        Assert.True(stack.TryResolveKey("key_b", out string? actBfn));
        Assert.Equal("action_b_fn_override", actBfn);

        // key_a falls through to base
        Assert.True(stack.TryResolveKey("key_a", out string? actAbase));
        Assert.Equal("action_a", actAbase);

        // key_d from fn
        Assert.True(stack.TryResolveKey("key_d", out string? actD));
        Assert.Equal("action_d_fn_only", actD);

        // non-existent
        Assert.False(stack.TryResolveKey("key_non_existent", out _));

        // Pop overlay
        Assert.True(stack.PopLayer("fn"));
        Assert.Single(stack.ActiveStack);

        // key_b reverts to base action
        Assert.True(stack.TryResolveKey("key_b", out string? actBrevert));
        Assert.Equal("action_b", actBrevert);
        Assert.False(stack.TryResolveKey("key_d", out _));
    }
}

