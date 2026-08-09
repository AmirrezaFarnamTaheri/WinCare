using System.Text;
using WinCare.Application.Native;

namespace WinCare.Infrastructure.Native;

/// <summary>
/// Safe, synchronous wrapper over the Rust-backed native primitive surface.
/// </summary>
public sealed class NativeCoreService : INativeCoreService
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

    /// <summary>
    /// Asynchronously accumulates the total byte size of all files under <paramref name="path"/>.
    /// Offloads the recursive traversal to a thread pool thread to keep the UI thread responsive.
    /// </summary>
    /// <exception cref="ArgumentException">path is null or whitespace.</exception>
    /// <exception cref="IOException">Native library returned a non-Ok status.</exception>
    /// <exception cref="OperationCanceledException">cancellationToken was cancelled.</exception>
    public Task<ulong> GetDirectorySizeAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(() => GetDirectorySize(path, cancellationToken), cancellationToken);
    }

    private static unsafe ulong GetDirectorySize(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        ulong size;
        fixed (byte* p = pathBytes)
        {
            int code = WinCareCoreNative.wincare_core_dir_size(p, (nuint)pathBytes.Length, &size);
            NativeCoreStatus status = (NativeCoreStatus)code;
            if (status != NativeCoreStatus.Ok)
            {
                throw CreateException(status, path, maxBytes: 0);
            }
        }
        ct.ThrowIfCancellationRequested();
        return size;
    }

    /// <summary>
    /// Asynchronously retrieves a JSON string with system facts (logical CPUs, memory, OS build).
    /// Uses a two-call probe-then-fill pattern matching <c>wincare_core_sys_info</c> ABI contract.
    /// </summary>
    /// <exception cref="InvalidOperationException">Native call failed unexpectedly.</exception>
    /// <exception cref="OperationCanceledException">cancellationToken was cancelled.</exception>
    public Task<string> GetSystemInfoJsonAsync(CancellationToken cancellationToken)
        => Task.Run(() => GetSystemInfoJson(cancellationToken), cancellationToken);

    private static unsafe string GetSystemInfoJson(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        nuint required = 0;
        var probeStatus = (NativeCoreStatus)WinCareCoreNative.wincare_core_sys_info(null, 0, &required);
        if (probeStatus != NativeCoreStatus.BufferTooSmall && probeStatus != NativeCoreStatus.Ok)
        {
            throw new InvalidOperationException($"wincare_core_sys_info failed with status {probeStatus} ({(int)probeStatus}).");
        }

        byte[] buf = new byte[(int)required];
        fixed (byte* p = buf)
        {
            nuint written = 0;
            int code = WinCareCoreNative.wincare_core_sys_info(p, (nuint)buf.Length, &written);
            NativeCoreStatus status = (NativeCoreStatus)code;
            if (status != NativeCoreStatus.Ok)
            {
                throw new InvalidOperationException(
                    $"wincare_core_sys_info failed with status {status} ({(int)status}).");
            }
            return Encoding.UTF8.GetString(buf, 0, (int)written);
        }
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
