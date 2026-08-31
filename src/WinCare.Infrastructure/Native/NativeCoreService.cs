using System.Buffers;
using System.Text;
using WinCare.Application.Native;

namespace WinCare.Infrastructure.Native;

/// <summary>
/// Safe, synchronous wrapper over the Rust-backed native primitive surface.
/// </summary>
public sealed class NativeCoreService : INativeCoreService
{
    private const int SystemInfoMaxAttempts = 3;

    /// <summary>
    /// Expected C ABI version exported by <c>wincare_core</c>.
    /// </summary>
    public const uint SupportedAbiVersion = 1;

    /// <summary>
    /// Returns the native C ABI version exposed by the Rust library.
    /// </summary>
    public uint GetAbiVersion() => WinCareCoreNative.WinCareCoreAbiVersion();

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
            int statusCode = WinCareCoreNative.WinCareCoreSha256File(
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
    /// Offloads traversal to a thread pool thread to keep the UI thread responsive.
    /// </summary>
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
            int code = WinCareCoreNative.WinCareCoreDirSize(p, (nuint)pathBytes.Length, &size);
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
    /// Uses a bounded probe/fill retry loop matching <c>wincare_core_sys_info</c> ABI contract.
    /// </summary>
    public Task<string> GetSystemInfoJsonAsync(CancellationToken cancellationToken)
        => Task.Run(() => GetSystemInfoJson(cancellationToken), cancellationToken);

    private static unsafe string GetSystemInfoJson(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        nuint required = 0;
        NativeCoreStatus probeStatus = (NativeCoreStatus)WinCareCoreNative.WinCareCoreSysInfo(null, 0, &required);
        if (probeStatus != NativeCoreStatus.BufferTooSmall && probeStatus != NativeCoreStatus.Ok)
        {
            throw SystemInfoException(probeStatus);
        }

        ValidateSystemInfoLength(required);

        for (int attempt = 0; attempt < SystemInfoMaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            int length = checked((int)required);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(length);

            try
            {
                fixed (byte* pointer = buffer)
                {
                    nuint written = 0;
                    NativeCoreStatus status = (NativeCoreStatus)WinCareCoreNative.WinCareCoreSysInfo(
                        pointer,
                        (nuint)buffer.Length,
                        &written);

                    if (status == NativeCoreStatus.Ok)
                    {
                        if (written > (nuint)buffer.Length)
                        {
                            throw new InvalidOperationException("wincare_core_sys_info wrote an invalid output length.");
                        }

                        return Encoding.UTF8.GetString(buffer, 0, checked((int)written));
                    }

                    if (status != NativeCoreStatus.BufferTooSmall)
                    {
                        throw SystemInfoException(status);
                    }

                    if (written <= (nuint)buffer.Length)
                    {
                        throw new InvalidOperationException("wincare_core_sys_info returned BufferTooSmall without increasing the required length.");
                    }

                    required = written;
                    ValidateSystemInfoLength(required);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        throw new InvalidOperationException("wincare_core_sys_info output changed repeatedly during bounded retries.");
    }

    private static void ValidateSystemInfoLength(nuint length)
    {
        if (length == 0 || length > int.MaxValue)
        {
            throw new InvalidOperationException("wincare_core_sys_info reported an invalid required buffer length.");
        }
    }

    private static InvalidOperationException SystemInfoException(NativeCoreStatus status) =>
        new($"wincare_core_sys_info failed with status {status} ({(int)status}).");

    private static Exception CreateException(NativeCoreStatus status, string path, ulong maxBytes) => status switch
    {
        NativeCoreStatus.NotFound => new FileNotFoundException("The file was not found.", path),
        NativeCoreStatus.FileTooLarge => new IOException($"The file exceeds the {maxBytes} byte limit."),
        NativeCoreStatus.InvalidUtf8 => new InvalidDataException("The file path could not be encoded as UTF-8."),
        NativeCoreStatus.BufferTooSmall => new InvalidOperationException("The native output contract is incompatible."),
        NativeCoreStatus.NullPointer => new InvalidOperationException("The native input contract rejected a pointer."),
        NativeCoreStatus.Truncated => new InvalidOperationException($"Directory sizing for '{path}' was truncated after reaching the entry limit or encountering unreadable entries."),
        _ => new IOException("The native file operation failed."),
    };
}
