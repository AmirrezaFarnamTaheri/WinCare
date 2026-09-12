using WinCare.Application.Storage;
using WinCare.Infrastructure.Storage;
using System.Runtime.InteropServices;

namespace WinCare.Infrastructure.Tests;

public sealed class StorageReportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wincare-storage-report-" + Guid.NewGuid().ToString("N"));
    private readonly StorageReportService _service = new();

    [Fact]
    public async Task Report_is_read_only_bounded_and_lists_largest_files()
    {
        Directory.CreateDirectory(Path.Combine(_root, "nested"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "small.bin"), new byte[3]);
        await File.WriteAllBytesAsync(Path.Combine(_root, "nested", "large.bin"), new byte[11]);

        StorageReport report = await _service.CreateReportAsync(new StorageReportRequest(_root, MaxEntries: 20, MaxDepth: 2, LargestFileCount: 2), CancellationToken.None);

        Assert.False(report.IsPartial);
        Assert.Equal<ulong>(14, report.AccountedBytes);
        Assert.Equal(2, report.FilesSeen);
        Assert.Equal(2, report.DirectoriesSeen);
        Assert.Equal("large.bin", Path.GetFileName(report.LargestFiles[0].Path));
        Assert.True(File.Exists(Path.Combine(_root, "nested", "large.bin")));
    }

    [Fact]
    public async Task Report_marks_entry_and_depth_limits_as_partial()
    {
        Directory.CreateDirectory(Path.Combine(_root, "one", "two"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "a.bin"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(_root, "b.bin"), [2]);

        StorageReport entries = await _service.CreateReportAsync(new StorageReportRequest(_root, MaxEntries: 1, MaxDepth: 4), CancellationToken.None);
        StorageReport depth = await _service.CreateReportAsync(new StorageReportRequest(_root, MaxEntries: 20, MaxDepth: 0), CancellationToken.None);

        Assert.True(entries.IsPartial);
        Assert.True(entries.ReachedEntryLimit);
        Assert.Contains(entries.Issues, issue => issue.Kind == StorageScanIssueKind.EntryLimit);
        Assert.True(depth.IsPartial);
        Assert.True(depth.ReachedDepthLimit);
        Assert.Contains(depth.Issues, issue => issue.Kind == StorageScanIssueKind.DepthLimit);
    }

    [Fact]
    public async Task Pre_cancelled_report_returns_a_cancelled_partial_result()
    {
        Directory.CreateDirectory(_root);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        StorageReport report = await _service.CreateReportAsync(new StorageReportRequest(_root), cancellation.Token);

        Assert.True(report.IsPartial);
        Assert.True(report.IsCancelled);
        Assert.Contains(report.Issues, issue => issue.Kind == StorageScanIssueKind.Cancelled);
    }

    [Fact]
    public async Task Missing_root_is_reported_instead_of_throwing()
    {
        StorageReport report = await _service.CreateReportAsync(new StorageReportRequest(_root), CancellationToken.None);

        Assert.True(report.IsPartial);
        Assert.Contains(report.Issues, issue => issue.Kind == StorageScanIssueKind.RootUnavailable);
    }

    [Fact]
    public async Task Reparse_directory_is_skipped_without_reading_its_target()
    {
        Directory.CreateDirectory(_root);
        string outside = Path.Combine(Path.GetTempPath(), "wincare-storage-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        await File.WriteAllBytesAsync(Path.Combine(outside, "sentinel.bin"), new byte[17]);
        try
        {
            try { Directory.CreateSymbolicLink(Path.Combine(_root, "outside-link"), outside); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return; } // Developer Mode / local policy may prohibit symlink creation.

            StorageReport report = await _service.CreateReportAsync(new StorageReportRequest(_root), CancellationToken.None);

            Assert.Equal<ulong>(0, report.AccountedBytes);
            Assert.Equal(1, report.ReparsePointsSkipped);
            Assert.DoesNotContain(report.LargestFiles, file => file.Path.Contains("sentinel.bin", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task Windows_hard_links_are_accounted_once()
    {
        if (!OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(_root);
        string original = Path.Combine(_root, "original.bin");
        string duplicate = Path.Combine(_root, "duplicate.bin");
        await File.WriteAllBytesAsync(original, new byte[9]);
        Assert.True(CreateHardLink(duplicate, original, IntPtr.Zero), Marshal.GetLastWin32Error().ToString());

        StorageReport report = await _service.CreateReportAsync(new StorageReportRequest(_root), CancellationToken.None);

        Assert.True(report.HardLinkAccountingComplete);
        Assert.Equal<ulong>(9, report.AccountedBytes);
        Assert.Equal(1, report.HardLinkDuplicatesSkipped);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
}
