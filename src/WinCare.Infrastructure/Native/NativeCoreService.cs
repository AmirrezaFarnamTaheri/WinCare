using System.Text;

namespace WinCare.Infrastructure.Native;

/// <summary>
/// Safe, synchronous wrapper over the Rust-backed native primitive surface.
/// </summary>
public sealed class NativeCoreService
{
    /// <summary>
    /// Expected C ABI version exported by <c>wincare_core</c>.
    /// </summary>
    public const uint SupportedAbiVersion = 1;

    /// <summary>
    /// Returns the native C ABI version exposed by the Rust library.
    /// </summary>
    public uint GetAbiVersion() => WinCareCoreNative.wincare_core_abi_version();

    /// <summary>
    /// Hashes a bounded file with SHA-256 through the native primitive.
    /// </summary>
    public Task<string> HashFileAsync(string path, ulong maxBytes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(() => HashFile(path, maxBytes, cancellationToken), cancellationToken);
    }

    private static unsafe string HashFile(string path, ulong maxBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        byte[] digest = new byte[32];

        fixed (byte* pathPointer = pathBytes)
        fixed (byte* digestPointer = digest)
        {
            int statusCode = WinCareCoreNative.wincare_core_sha256_file(
                pathPointer,
                (nuint)pathBytes.Length,
                maxBytes,
                digestPointer,
                (nuint)digest.Length);

            NativeCoreStatus status = (NativeCoreStatus)statusCode;
            if (status != NativeCoreStatus.Ok)
            {
                throw CreateException(status, path, maxBytes);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static Exception CreateException(NativeCoreStatus status, string path, ulong maxBytes) => status switch
    {
        NativeCoreStatus.NotFound => new FileNotFoundException("The file was not found.", path),
        NativeCoreStatus.FileTooLarge => new IOException($"The file exceeds the {maxBytes} byte limit."),
        NativeCoreStatus.InvalidUtf8 => new InvalidDataException("The file path could not be encoded as UTF-8."),
        NativeCoreStatus.BufferTooSmall => new InvalidOperationException("The native output contract is incompatible."),
        NativeCoreStatus.NullPointer => new InvalidOperationException("The native input contract rejected a pointer."),
        _ => new IOException("The native file operation failed."),
    };
}
