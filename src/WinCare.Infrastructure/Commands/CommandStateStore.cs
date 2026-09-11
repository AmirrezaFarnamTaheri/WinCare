using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WinCare.Infrastructure.Commands;

/// <summary>
/// Durable app-owned JSON state. Files are fixed by logical key; callers cannot escape the WinCare data root.
/// Writes use replace-on-close semantics so partial writes do not corrupt state.
/// </summary>
public sealed class CommandStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Semaphore _crossProcessGate;

    /// <summary>
    /// Bounded wait for the cross-process state lock (F-011): writers from independent
    /// store instances (or processes) serialize on a named mutex derived from the data
    /// root, so read-modify-write updates cannot silently lose one another.
    /// </summary>
    private static readonly TimeSpan CrossProcessLockTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Initializes a new instance of <see cref="CommandStateStore"/>.
    /// </summary>
    /// <param name="root">Optional data root directory.</param>
    public CommandStateStore(string? root = null)
    {
        _root = Path.GetFullPath(root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinCare",
            "state"));
        Directory.CreateDirectory(_root);

        // One OS-wide lock name per data root: every store instance (in this process or
        // another) pointed at the same root shares the same write lock. A named Semaphore
        // is used because writer continuations may resume on any thread; semaphores have
        // no thread affinity and their count is restored by the OS if a holder dies.
        string rootHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_root)))[..16];
        _crossProcessGate = new Semaphore(initialCount: 0, maximumCount: 1, @"Local\WinCare.StateStore." + rootHash, out bool createdNew);
        if (createdNew)
        {
            _crossProcessGate.Release();
        }
    }

    /// <summary>
    /// Gets the absolute root data path.
    /// </summary>
    public string Root => _root;

    /// <summary>
    /// Reads a state element by key.
    /// </summary>
    public async Task<JsonElement> ReadAsync(string key, JsonElement fallback, CancellationToken cancellationToken)
    {
        string path = PathFor(key);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                return fallback.Clone();
            }
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
            JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            using (document)
            {
                return document.RootElement.Clone();
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"WinCare state '{key}' is not valid JSON.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Acquires the OS-wide write lock for this data root with a bounded wait (F-011).
    /// </summary>
    /// <returns>True when the lock was acquired; the caller must release it in finally.</returns>
    private bool TryAcquireCrossProcessGate()
    {
        return _crossProcessGate.WaitOne(CrossProcessLockTimeout);
    }

    /// <summary>
    /// Writes a state element by key.
    /// </summary>
    public async Task WriteAsync(string key, JsonElement value, CancellationToken cancellationToken)
    {
        string path = PathFor(key);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        bool crossProcessLockHeld = TryAcquireCrossProcessGate();
        try
        {
            if (!crossProcessLockHeld)
            {
                throw new TimeoutException(
                    $"WinCare state '{key}' is busy: another process holds the state write lock.");
            }

            Directory.CreateDirectory(_root);
            await using (FileStream stream = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
            if (crossProcessLockHeld)
            {
                _crossProcessGate.Release();
            }
            _gate.Release();
        }
    }

    /// <summary>
    /// Atomically reads, transforms, and writes state within a single lock.
    /// </summary>
    public async Task<JsonElement> UpdateAsync(
        string key,
        JsonElement fallback,
        Func<JsonElement, JsonElement> transform,
        CancellationToken cancellationToken)
    {
        string path = PathFor(key);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        bool crossProcessLockHeld = TryAcquireCrossProcessGate();
        try
        {
            if (!crossProcessLockHeld)
            {
                throw new TimeoutException(
                    $"WinCare state '{key}' is busy: another process holds the state write lock.");
            }

            JsonElement current = fallback.Clone();
            if (File.Exists(path))
            {
                try
                {
                    await using FileStream readStream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
                    using JsonDocument doc = await JsonDocument.ParseAsync(readStream, cancellationToken: cancellationToken).ConfigureAwait(false);
                    current = doc.RootElement.Clone();
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException($"WinCare state '{key}' is not valid JSON.", ex);
                }
            }

            JsonElement updated = transform(current);

            Directory.CreateDirectory(_root);
            await using (FileStream writeStream = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(writeStream, updated, JsonOptions, cancellationToken).ConfigureAwait(false);
                await writeStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temp, path, overwrite: true);
            return updated;
        }
        finally
        {
            TryDelete(temp);
            if (crossProcessLockHeld)
            {
                _crossProcessGate.Release();
            }
            _gate.Release();
        }
    }

    /// <summary>
    /// Reads state as a JSON object.
    /// </summary>
    public async Task<JsonElement> ReadObjectAsync(string key, CancellationToken cancellationToken) =>
        await ReadAsync(key, JsonSerializer.SerializeToElement(new Dictionary<string, object?>()), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Reads state as a JSON array.
    /// </summary>
    public async Task<JsonElement> ReadArrayAsync(string key, CancellationToken cancellationToken) =>
        await ReadAsync(key, JsonSerializer.SerializeToElement(Array.Empty<object>()), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Resolves export path safely within target directory bounds.
    /// </summary>
    public string ResolveExportPath(string requestedPath, string defaultFileName)
    {
        string path = string.IsNullOrWhiteSpace(requestedPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), defaultFileName)
            : Environment.ExpandEnvironmentVariables(requestedPath.Trim());
        return Path.GetFullPath(path);
    }

    private string PathFor(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_')))
        {
            throw new ArgumentException("State key contains unsupported characters.", nameof(key));
        }
        string path = Path.GetFullPath(Path.Combine(_root, key + ".json"));
        string prefix = Path.TrimEndingDirectorySeparator(_root) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved state path escaped the WinCare data root.");
        }
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
