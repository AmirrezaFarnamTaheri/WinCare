using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using WinCare.Application.Transfers;

namespace WinCare.Infrastructure.Torrents;

/// <summary>
/// Manages high-performance sparse disk allocations and piece-level SHA-1 verification.
/// Uses Win32 FSCTL_SET_SPARSE to avoid synchronous OS zeroing stalls on large files.
/// </summary>
public sealed class TorrentStorageManager : IAsyncDisposable
{
    private const uint FSCTL_SET_SPARSE = 0x000900C4;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        nint lpInBuffer,
        uint nInBufferSize,
        nint lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        nint lpOverlapped);

    private readonly TorrentMetadata _metadata;
    private readonly string _baseDirectory;

    public TorrentStorageManager(TorrentMetadata metadata, string baseDirectory)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
    }

    /// <summary>
    /// Pre-allocates destination files as sparse NTFS files.
    /// </summary>
    public async Task InitializeFilesAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_baseDirectory))
        {
            Directory.CreateDirectory(_baseDirectory);
        }

        foreach (TorrentFileEntry file in _metadata.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath = Path.Combine(_baseDirectory, file.RelativePath);
            string? dir = Path.GetDirectoryName(fullPath);
            if (dir is not null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!File.Exists(fullPath))
            {
                await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                TryMarkSparse(fs.SafeFileHandle);
                if (file.Length > 0)
                {
                    fs.SetLength(file.Length);
                }
            }
        }
    }

    /// <summary>
    /// Writes an incoming piece payload and verifies its SHA-1 hash against the metadata.
    /// </summary>
    public async Task<bool> WriteAndVerifyPieceAsync(int pieceIndex, ReadOnlyMemory<byte> pieceData, CancellationToken cancellationToken = default)
    {
        if (pieceIndex < 0 || pieceIndex >= _metadata.PieceCount) return false;

        // Verify SHA-1 hash first
        byte[] expectedHash = _metadata.PieceHashes[pieceIndex];
        byte[] actualHash = SHA1.HashData(pieceData.Span);

        if (!actualHash.AsSpan().SequenceEqual(expectedHash))
        {
            return false;
        }

        // Write piece to appropriate files
        long globalOffset = (long)pieceIndex * _metadata.PieceLength;
        int remainingBytes = pieceData.Length;
        int bufferOffset = 0;

        foreach (TorrentFileEntry file in _metadata.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long fileStart = file.Offset;
            long fileEnd = file.Offset + file.Length;

            if (globalOffset + remainingBytes <= fileStart || globalOffset >= fileEnd)
            {
                continue; // Piece does not touch this file
            }

            long writeStartInFile = Math.Max(0, globalOffset - fileStart);
            int writeLength = (int)Math.Min(remainingBytes, file.Length - writeStartInFile);

            string fullPath = Path.Combine(_baseDirectory, file.RelativePath);
            await using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            fs.Seek(writeStartInFile, SeekOrigin.Begin);
            await fs.WriteAsync(pieceData.Slice(bufferOffset, writeLength), cancellationToken).ConfigureAwait(false);

            bufferOffset += writeLength;
            remainingBytes -= writeLength;
            globalOffset += writeLength;

            if (remainingBytes <= 0) break;
        }

        return true;
    }

    private static void TryMarkSparse(SafeFileHandle handle)
    {
        try
        {
            DeviceIoControl(handle, FSCTL_SET_SPARSE, nint.Zero, 0, nint.Zero, 0, out _, nint.Zero);
        }
        catch
        {
            // Non-fatal if filesystem does not support sparse files (e.g. FAT32/exFAT)
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
