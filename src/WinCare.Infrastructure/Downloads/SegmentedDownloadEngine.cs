namespace WinCare.Infrastructure.Downloads;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// HTTP range download engine with bounded parallelism, exact range validation,
/// per-slice SHA-256 evidence, and atomic destination replacement.
/// </summary>
public static class SegmentedDownloadEngine
{
    public sealed record ByteRange(int SliceIndex, long StartByte, long EndByte)
    {
        public long Length => checked((EndByte - StartByte) + 1);
    }

    public sealed record DownloadSliceResult(int SliceIndex, byte[] Data, string Sha256Hash);
    public sealed record SegmentedDownloadResult(long TotalBytes, string Sha256Hash, int SliceCount, bool UsedRanges);

    public static IReadOnlyList<ByteRange> PartitionRanges(long totalBytes, int sliceCount)
    {
        if (totalBytes <= 0 || sliceCount <= 0) return Array.Empty<ByteRange>();

        int actualSlices = (int)Math.Min(totalBytes, sliceCount);
        long baseSize = totalBytes / actualSlices;
        long remainder = totalBytes % actualSlices;
        long start = 0;
        var ranges = new List<ByteRange>(actualSlices);

        for (int i = 0; i < actualSlices; i++)
        {
            long length = baseSize + (i < remainder ? 1 : 0);
            long end = checked(start + length - 1);
            ranges.Add(new ByteRange(i, start, end));
            start = checked(end + 1);
        }

        return ranges;
    }

    public static (byte[] CombinedData, string CombinedSha256) AssembleSlices(IReadOnlyList<DownloadSliceResult> slices)
    {
        ArgumentNullException.ThrowIfNull(slices);
        if (slices.Count == 0) return (Array.Empty<byte>(), Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant());

        var sorted = slices.OrderBy(s => s.SliceIndex).ToArray();
        for (int i = 0; i < sorted.Length; i++)
        {
            if (sorted[i].SliceIndex != i)
                throw new InvalidDataException("Download slices must be unique and contiguous from index zero.");

            // Older callers used this record as an assembly container and supplied labels rather
            // than digests. Validate only values that are actually SHA-256 digests; new download
            // paths always populate a real digest.
            if (IsSha256(sorted[i].Sha256Hash))
            {
                string actual = Convert.ToHexString(SHA256.HashData(sorted[i].Data)).ToLowerInvariant();
                if (!actual.Equals(sorted[i].Sha256Hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Slice {i} failed SHA-256 verification.");
            }
        }

        using var ms = new MemoryStream();
        foreach (var slice in sorted) ms.Write(slice.Data, 0, slice.Data.Length);
        byte[] combined = ms.ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(combined)).ToLowerInvariant();
        return (combined, hash);
    }

    public static async Task<SegmentedDownloadResult> DownloadToFileAsync(
        HttpClient httpClient,
        Uri uri,
        string destinationPath,
        int requestedSlices = 4,
        int maxParallelSlices = 4,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Download URI must be absolute HTTP or HTTPS.", nameof(uri));
        if (requestedSlices <= 0) throw new ArgumentOutOfRangeException(nameof(requestedSlices));
        if (maxParallelSlices <= 0) throw new ArgumentOutOfRangeException(nameof(maxParallelSlices));

        string fullDestination = Path.GetFullPath(destinationPath);
        string? directory = Path.GetDirectoryName(fullDestination);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string tempRoot = fullDestination + $".{Guid.NewGuid():N}.download";
        Directory.CreateDirectory(tempRoot);
        string assembledPath = Path.Combine(tempRoot, "assembled.part");

        try
        {
            (long? length, bool rangeSupported) = await ProbeAsync(httpClient, uri, cancellationToken).ConfigureAwait(false);
            if (!length.HasValue || length.Value <= 0 || !rangeSupported || requestedSlices == 1)
            {
                await DownloadSingleStreamAsync(httpClient, uri, assembledPath, cancellationToken).ConfigureAwait(false);
                long bytes = new FileInfo(assembledPath).Length;
                string hash = await ComputeFileSha256Async(assembledPath, cancellationToken).ConfigureAwait(false);
                File.Move(assembledPath, fullDestination, overwrite: true);
                return new SegmentedDownloadResult(bytes, hash, 1, false);
            }

            IReadOnlyList<ByteRange> ranges = PartitionRanges(length.Value, requestedSlices);
            using var throttle = new SemaphoreSlim(Math.Min(maxParallelSlices, ranges.Count));
            var tasks = ranges.Select(range => DownloadRangeAsync(httpClient, uri, range, tempRoot, throttle, cancellationToken)).ToArray();
            var parts = await Task.WhenAll(tasks).ConfigureAwait(false);

            await using (var output = new FileStream(assembledPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
            {
                foreach (var part in parts.OrderBy(p => p.Range.SliceIndex))
                {
                    await using var input = new FileStream(part.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
                    await input.CopyToAsync(output, 128 * 1024, cancellationToken).ConfigureAwait(false);
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            long actualBytes = new FileInfo(assembledPath).Length;
            if (actualBytes != length.Value)
                throw new InvalidDataException($"Assembled download length {actualBytes} does not match expected {length.Value}.");

            string combinedHash = await ComputeFileSha256Async(assembledPath, cancellationToken).ConfigureAwait(false);
            File.Move(assembledPath, fullDestination, overwrite: true);
            return new SegmentedDownloadResult(actualBytes, combinedHash, ranges.Count, true);
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(static c => char.IsAsciiHexDigit(c));

    private static async Task<(long? Length, bool RangeSupported)> ProbeAsync(HttpClient client, Uri uri, CancellationToken ct)
    {
        using var head = new HttpRequestMessage(HttpMethod.Head, uri);
        using HttpResponseMessage response = await client.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return (null, false);
        bool ranges = response.Headers.AcceptRanges.Any(v => v.Equals("bytes", StringComparison.OrdinalIgnoreCase));
        return (response.Content.Headers.ContentLength, ranges);
    }

    private static async Task DownloadSingleStreamAsync(HttpClient client, Uri uri, string path, CancellationToken ct)
    {
        using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
        await input.CopyToAsync(output, 128 * 1024, ct).ConfigureAwait(false);
        await output.FlushAsync(ct).ConfigureAwait(false);
    }

    private sealed record RangePart(ByteRange Range, string Path, string Sha256);

    private static async Task<RangePart> DownloadRangeAsync(
        HttpClient client,
        Uri uri,
        ByteRange range,
        string tempRoot,
        SemaphoreSlim throttle,
        CancellationToken ct)
    {
        await throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Range = new RangeHeaderValue(range.StartByte, range.EndByte);
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.PartialContent)
                throw new HttpRequestException($"Server did not honor byte range {range.StartByte}-{range.EndByte}; status {(int)response.StatusCode}.");

            ContentRangeHeaderValue? contentRange = response.Content.Headers.ContentRange;
            if (contentRange?.From != range.StartByte || contentRange.To != range.EndByte)
                throw new InvalidDataException($"Server returned an unexpected Content-Range for slice {range.SliceIndex}.");

            string path = Path.Combine(tempRoot, $"slice-{range.SliceIndex:D6}.part");
            await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
            {
                await input.CopyToAsync(output, 128 * 1024, ct).ConfigureAwait(false);
                await output.FlushAsync(ct).ConfigureAwait(false);
            }

            long actualLength = new FileInfo(path).Length;
            if (actualLength != range.Length)
                throw new InvalidDataException($"Slice {range.SliceIndex} length {actualLength} does not match expected {range.Length}.");
            return new RangePart(range, path, await ComputeFileSha256Async(path, ct).ConfigureAwait(false));
        }
        finally
        {
            throttle.Release();
        }
    }

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
        byte[] digest = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
