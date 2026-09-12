using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WinCare.Application.Storage;

namespace WinCare.Infrastructure.Storage;

/// <summary>
/// Iterative, bounded directory scanner. It never writes, follows reparse points, or recurses on the call stack.
/// </summary>
public sealed class StorageReportService : IStorageReportService
{
    /// <inheritdoc />
    public Task<StorageReport> CreateReportAsync(StorageReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        return Task.Run(() => Scan(request, cancellationToken));
    }

    private static StorageReport Scan(StorageReportRequest request, CancellationToken cancellationToken)
    {
        string root;
        try { root = Path.GetFullPath(request.RootPath); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failed(request.RootPath, ex.Message);
        }

        var issues = new List<StorageScanIssue>();
        FileAttributes rootAttributes;
        try { rootAttributes = File.GetAttributes(root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed(root, ex.Message);
        }
        if ((rootAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
        {
            return Failed(root, "The scan root must be a non-reparse directory.");
        }

        var directories = new List<DirectoryAccumulator> { new(root, null, 0) };
        var pending = new Stack<PendingDirectory>();
        pending.Push(new(root, 0, 0));
        var largest = new List<StorageFileEntry>();
        var identities = new HashSet<FileIdentity>();
        int entries = 0, files = 0, dirs = 1, reparses = 0, hardLinkDuplicates = 0;
        bool partial = false, cancelled = false, entryLimit = false, depthLimit = false, identitiesComplete = OperatingSystem.IsWindows();

        while (pending.Count > 0 && !entryLimit && !cancelled)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = partial = true;
                issues.Add(new StorageScanIssue(root, StorageScanIssueKind.Cancelled, "The scan was cancelled by the caller."));
                break;
            }

            PendingDirectory current = pending.Pop();
            try
            {
                foreach (string path in Directory.EnumerateFileSystemEntries(current.Path))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancelled = partial = true;
                        issues.Add(new StorageScanIssue(current.Path, StorageScanIssueKind.Cancelled, "The scan was cancelled by the caller."));
                        break;
                    }
                    if (entries >= request.MaxEntries)
                    {
                        entryLimit = partial = true;
                        issues.Add(new StorageScanIssue(current.Path, StorageScanIssueKind.EntryLimit, $"The {request.MaxEntries:N0} entry limit was reached."));
                        break;
                    }
                    entries++;

                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(path); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        partial = true;
                        issues.Add(new StorageScanIssue(path, StorageScanIssueKind.Access, ex.Message));
                        continue;
                    }
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        reparses++;
                        continue;
                    }
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        dirs++;
                        int depth = current.Depth + 1;
                        if (depth > request.MaxDepth)
                        {
                            depthLimit = partial = true;
                            issues.Add(new StorageScanIssue(path, StorageScanIssueKind.DepthLimit, $"The configured depth limit of {request.MaxDepth} was reached."));
                            continue;
                        }
                        int directoryIndex = directories.Count;
                        directories.Add(new DirectoryAccumulator(path, current.Path, depth));
                        pending.Push(new(path, depth, directoryIndex));
                        continue;
                    }

                    files++;
                    ulong bytes;
                    try { bytes = checked((ulong)new FileInfo(path).Length); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        partial = true;
                        issues.Add(new StorageScanIssue(path, StorageScanIssueKind.Access, ex.Message));
                        continue;
                    }

                    if (OperatingSystem.IsWindows())
                    {
                        if (!TryGetWindowsFileIdentity(path, out FileIdentity identity, out string? identityError))
                        {
                            identitiesComplete = false;
                            partial = true;
                            issues.Add(new StorageScanIssue(path, StorageScanIssueKind.FileIdentity, identityError ?? "File identity could not be read."));
                        }
                        else if (!identities.Add(identity))
                        {
                            hardLinkDuplicates++;
                            continue;
                        }
                    }

                    AddBytesToAncestors(directories, current.DirectoryIndex, bytes);
                    InsertLargest(largest, new StorageFileEntry(path, bytes), request.LargestFileCount);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                partial = true;
                issues.Add(new StorageScanIssue(current.Path, StorageScanIssueKind.Access, ex.Message));
            }
        }

        return new StorageReport(root, directories[0].Bytes, entries, files, dirs, reparses, hardLinkDuplicates,
            identitiesComplete, partial, cancelled, entryLimit, depthLimit,
            directories.Select(directory => new StorageDirectoryEntry(directory.Path, directory.ParentPath, directory.Depth, directory.Bytes)).ToArray(),
            largest.ToArray(), issues.ToArray());
    }

    private static StorageReport Failed(string root, string message) => new(root, 0, 0, 0, 0, 0, 0,
        OperatingSystem.IsWindows(), true, false, false, false, [], [],
        [new StorageScanIssue(root, StorageScanIssueKind.RootUnavailable, message)]);

    private static void AddBytesToAncestors(List<DirectoryAccumulator> directories, int index, ulong bytes)
    {
        for (int current = index; current >= 0; current = ParentIndex(directories, current))
        {
            directories[current].Bytes = directories[current].Bytes > ulong.MaxValue - bytes ? ulong.MaxValue : directories[current].Bytes + bytes;
        }
    }

    private static int ParentIndex(List<DirectoryAccumulator> directories, int index)
    {
        string? parent = directories[index].ParentPath;
        if (parent is null) return -1;
        for (int candidate = index - 1; candidate >= 0; candidate--)
            if (string.Equals(directories[candidate].Path, parent, StringComparison.OrdinalIgnoreCase)) return candidate;
        return -1;
    }

    private static void InsertLargest(List<StorageFileEntry> largest, StorageFileEntry entry, int capacity)
    {
        int index = largest.FindIndex(existing => existing.Bytes < entry.Bytes || (existing.Bytes == entry.Bytes && string.CompareOrdinal(existing.Path, entry.Path) > 0));
        if (index < 0) index = largest.Count;
        if (index >= capacity) return;
        largest.Insert(index, entry);
        if (largest.Count > capacity) largest.RemoveAt(largest.Count - 1);
    }

    private sealed class DirectoryAccumulator(string path, string? parentPath, int depth)
    {
        public string Path { get; } = path;
        public string? ParentPath { get; } = parentPath;
        public int Depth { get; } = depth;
        public ulong Bytes { get; set; }
    }

    private readonly record struct PendingDirectory(string Path, int Depth, int DirectoryIndex);
    private readonly record struct FileIdentity(uint VolumeSerial, uint FileIndexHigh, uint FileIndexLow);

    private static bool TryGetWindowsFileIdentity(string path, out FileIdentity identity, out string? error)
    {
        const uint FileReadAttributes = 0x80, FileShareRead = 1, FileShareWrite = 2, FileShareDelete = 4, OpenExisting = 3, FileFlagBackupSemantics = 0x02000000;
        using SafeFileHandle handle = CreateFile(path, FileReadAttributes, FileShareRead | FileShareWrite | FileShareDelete, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid) { identity = default; error = Marshal.GetLastWin32Error().ToString(System.Globalization.CultureInfo.InvariantCulture); return false; }
        if (!GetFileInformationByHandle(handle, out ByHandleFileInformation info)) { identity = default; error = Marshal.GetLastWin32Error().ToString(System.Globalization.CultureInfo.InvariantCulture); return false; }
        identity = new FileIdentity(info.VolumeSerialNumber, info.FileIndexHigh, info.FileIndexLow); error = null; return true;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr templateFile);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);
    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation { public uint FileAttributes, CreationTimeLow, CreationTimeHigh, LastAccessTimeLow, LastAccessTimeHigh, LastWriteTimeLow, LastWriteTimeHigh, VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow; }
}
