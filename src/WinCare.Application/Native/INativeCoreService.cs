namespace WinCare.Application.Native;

/// <summary>
/// Service contract for native C-ABI primitives (file hashing, directory size, system info).
/// Implemented by <c>WinCare.Infrastructure.Native.NativeCoreService</c>.
/// </summary>
public interface INativeCoreService
{
    /// <summary>
    /// Returns the native C ABI version exposed by the underlying Rust core library.
    /// </summary>
    uint GetAbiVersion();

    /// <summary>
    /// Hashes a bounded file with SHA-256 through the native primitive.
    /// </summary>
    Task<string> HashFileAsync(string path, ulong maxBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Asynchronously accumulates the total byte size of all files under <paramref name="path"/>.
    /// </summary>
    Task<ulong> GetDirectorySizeAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Asynchronously retrieves a JSON string with system facts (logical CPUs, memory, OS build).
    /// </summary>
    Task<string> GetSystemInfoJsonAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Asynchronously accumulates comprehensive statistics (total bytes, file count, dir count) for a directory.
    /// </summary>
    Task<NativeDirectoryStats> GetDirectoryStatsAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Cryptographically overwrites and securely deletes a file using multi-pass patterns.
    /// </summary>
    Task ShredFileAsync(string path, uint passes, CancellationToken cancellationToken);

    /// <summary>
    /// Queries whether the volume underlying <paramref name="driveLetter"/> incurs a seek penalty (rotational vs SSD).
    /// </summary>
    bool? QuerySeekPenalty(char driveLetter);

    /// <summary>
    /// Optimizes NT system memory lists using the specified bitmask.
    /// Returns the number of physical RAM bytes freed.
    /// </summary>
    ulong OptimizeMemoryLists(uint mask);

    /// <summary>
    /// Broadcasts a shell change notification to refresh Explorer caches.
    /// </summary>
    void BroadcastShellNotify();

    /// <summary>
    /// Queries Desktop Window Manager (DWM) whether a window handle is cloaked.
    /// Returns true if cloaked, false if uncloaked, or null if query fails.
    /// </summary>
    bool? IsWindowCloaked(nint hwnd);
}

/// <summary>
/// Aggregated directory traversal statistics returned by native inspection.
/// </summary>
public sealed record NativeDirectoryStats(ulong TotalBytes, ulong FileCount, ulong DirCount, bool IsComplete);
