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
}
