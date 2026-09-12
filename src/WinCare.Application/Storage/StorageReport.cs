namespace WinCare.Application.Storage;

/// <summary>Limits for a read-only storage scan.</summary>
public sealed record StorageReportRequest(string RootPath, int MaxEntries = 25_000, int MaxDepth = 6, int LargestFileCount = 25)
{
    /// <summary>Validates bounded scan inputs before filesystem access.</summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RootPath);
        if (MaxEntries is < 1 or > 100_000) throw new ArgumentOutOfRangeException(nameof(MaxEntries));
        if (MaxDepth is < 0 or > 64) throw new ArgumentOutOfRangeException(nameof(MaxDepth));
        if (LargestFileCount is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(LargestFileCount));
    }
}

/// <summary>A bounded, truthful view of a directory scan.</summary>
public sealed record StorageReport(
    string RootPath,
    ulong AccountedBytes,
    int EntriesScanned,
    int FilesSeen,
    int DirectoriesSeen,
    int ReparsePointsSkipped,
    int HardLinkDuplicatesSkipped,
    bool HardLinkAccountingComplete,
    bool IsPartial,
    bool IsCancelled,
    bool ReachedEntryLimit,
    bool ReachedDepthLimit,
    IReadOnlyList<StorageDirectoryEntry> Directories,
    IReadOnlyList<StorageFileEntry> LargestFiles,
    IReadOnlyList<StorageScanIssue> Issues);

/// <summary>A directory node in the discovered tree. Bytes include scanned descendants.</summary>
public sealed record StorageDirectoryEntry(string Path, string? ParentPath, int Depth, ulong AccountedBytes);

/// <summary>A file retained in the bounded largest-file list.</summary>
public sealed record StorageFileEntry(string Path, ulong Bytes);

/// <summary>An access, metadata, or scan-limit issue encountered while producing a report.</summary>
public sealed record StorageScanIssue(string Path, StorageScanIssueKind Kind, string Message);

/// <summary>Classification for a report issue.</summary>
public enum StorageScanIssueKind
{
    /// <summary>The selected root could not be scanned.</summary>
    RootUnavailable,
    /// <summary>An entry could not be inspected or enumerated.</summary>
    Access,
    /// <summary>The bounded entry budget was exhausted.</summary>
    EntryLimit,
    /// <summary>A directory beyond the configured scan depth was omitted.</summary>
    DepthLimit,
    /// <summary>File identity could not be read, so hard-link accounting is incomplete.</summary>
    FileIdentity,
    /// <summary>The caller requested cancellation.</summary>
    Cancelled,
}
