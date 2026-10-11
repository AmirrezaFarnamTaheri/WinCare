using System.Globalization;
using System.Security;

namespace WinCare.Application.Storage;

/// <summary>A local volume the user can browse without granting mutation privileges.</summary>
public sealed record StorageBrowserDrive(string Root, string Label, string SpaceLabel, double UsedPercent);

/// <summary>A single real filesystem entry; folder sizes are never guessed.</summary>
public sealed record StorageBrowserItem(
    string Name, string FullPath, bool IsFolder, bool IsLink, string Kind,
    long? Bytes, DateTime ModifiedUtc)
{
    public string SizeLabel => IsFolder ? "—" : Bytes is long bytes ? StorageExplorer.FormatBytes((ulong)bytes) : "Unknown";
    public string ModifiedLabel => ModifiedUtc == DateTime.MinValue ? "Unknown" : ModifiedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public string Glyph => IsFolder ? "\uE8B7" : "\uE8A5";
    public string AccessibleName => $"{Name}, {Kind}, {SizeLabel}";
}

/// <summary>A bounded directory snapshot, not an unbounded recursive disk scan.</summary>
public sealed record StorageBrowserListing(
    string Path, IReadOnlyList<StorageBrowserItem> Items, bool Truncated, int UnreadableEntries);

/// <summary>
/// Read-only file navigation. No deletion, writes, symbolic-link traversal, or background
/// indexing. The caller runs Browse off the UI thread; the entry cap bounds memory.
/// </summary>
public static class StorageExplorer
{
    public static IReadOnlyList<StorageBrowserDrive> Drives()
    {
        var drives = new List<StorageBrowserDrive>();
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                long capacity = drive.TotalSize, free = drive.AvailableFreeSpace;
                double used = capacity > 0 ? Math.Clamp(100d * (capacity - free) / capacity, 0, 100) : 0;
                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? drive.Name : $"{drive.Name}  {drive.VolumeLabel}";
                drives.Add(new StorageBrowserDrive(drive.RootDirectory.FullName, label,
                    $"{FormatBytes((ulong)Math.Max(0, free))} free of {FormatBytes((ulong)Math.Max(0, capacity))}", used));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // Optical, disconnected, or permission-restricted volumes are not browsable.
            }
        }
        return drives.OrderBy(drive => drive.Root, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static StorageBrowserListing Browse(string path, int maxEntries = 600)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maxEntries is < 1 or > 2_000) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        string fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException($"Folder not found: {fullPath}");

        var entries = new List<StorageBrowserItem>();
        int unreadable = 0;
        bool truncated = false;
        foreach (FileSystemInfo entry in new DirectoryInfo(fullPath).EnumerateFileSystemInfos())
        {
            if (entries.Count >= maxEntries)
            {
                truncated = true;
                break;
            }
            try
            {
                FileAttributes attributes = entry.Attributes;
                bool folder = (attributes & FileAttributes.Directory) != 0;
                bool link = (attributes & FileAttributes.ReparsePoint) != 0;
                long? bytes = folder ? null : ((FileInfo)entry).Length;
                string kind = link ? "Link" : folder ? "Folder" :
                    string.IsNullOrEmpty(entry.Extension) ? "File" : entry.Extension.TrimStart('.').ToUpperInvariant() + " file";
                entries.Add(new StorageBrowserItem(entry.Name, entry.FullName, folder, link, kind,
                    bytes, entry.LastWriteTimeUtc));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                unreadable++;
            }
        }
        return new StorageBrowserListing(fullPath,
            entries.OrderByDescending(entry => entry.IsFolder)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            truncated, unreadable);
    }

    public static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{bytes:N0} B" : $"{size:N1} {units[unit]}";
    }
}
