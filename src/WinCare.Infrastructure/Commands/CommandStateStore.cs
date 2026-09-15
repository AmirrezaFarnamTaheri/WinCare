using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WinCare.Infrastructure.Commands;

/// <summary>
/// Durable app-owned JSON state. Files are fixed by logical key; callers cannot escape the WinCare data root.
/// Updates are serialized per root within the process and across processes, then committed through a temporary
/// file so a failed write cannot expose partial JSON.
/// </summary>
public sealed class CommandStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProcessWriteGates =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _processWriteGate;
    private readonly Semaphore _crossProcessGate;

    /// <summary>
    /// Bounded wait for the cross-process state lock.
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

        // Multiple CommandStateStore instances can exist in one app process. Serialize their
        // read-transform-write transactions before entering the OS-wide lock so they cannot
        // race each other's temporary-file replacement even if the platform's named-object
        // implementation behaves differently across hosts/test runners.
        _processWriteGate = ProcessWriteGates.GetOrAdd(
            _root,
            static _ => new SemaphoreSlim(initialCount: 1, maxCount: 1));

        // One OS-wide lock name per data root: every process pointed at the same root shares
        // the same write lock. A named Semaphore is used because async continuations can resume
        // on any thread; semaphores have no thread affinity and the OS restores the count if a
        // holder process exits unexpectedly.
        string rootHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_root)))[..16];
        _crossProcessGate = new Semaphore(initialCount: 1, maximumCount: 1, @"Local\WinCare.StateStore." + rootHash);
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
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 64 * 1024, useAsync: true);
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
    /// Acquires the OS-wide write lock for this data root with a bounded wait.
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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        bool processLockHeld = false;
        bool crossProcessLockHeld = false;
        try
        {
            await _processWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            processLockHeld = true;
            crossProcessLockHeld = TryAcquireCrossProcessGate();
            if (!crossProcessLockHeld)
            {
                throw new TimeoutException(
                    $"WinCare state '{key}' is busy: another process holds the state write lock.");
            }

            await CommitAsync(key, path, value, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (crossProcessLockHeld)
            {
                _crossProcessGate.Release();
            }
            if (processLockHeld)
            {
                _processWriteGate.Release();
            }
            _gate.Release();
        }
    }

    /// <summary>
    /// Atomically reads, transforms, and writes state within a single transaction lock.
    /// </summary>
    public async Task<JsonElement> UpdateAsync(
        string key,
        JsonElement fallback,
        Func<JsonElement, JsonElement> transform,
        CancellationToken cancellationToken)
    {
        string path = PathFor(key);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        bool processLockHeld = false;
        bool crossProcessLockHeld = false;
        try
        {
            await _processWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            processLockHeld = true;
            crossProcessLockHeld = TryAcquireCrossProcessGate();
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
                    await using FileStream readStream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 64 * 1024, useAsync: true);
                    using JsonDocument doc = await JsonDocument.ParseAsync(readStream, cancellationToken: cancellationToken).ConfigureAwait(false);
                    current = doc.RootElement.Clone();
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException($"WinCare state '{key}' is not valid JSON.", ex);
                }
            }

            JsonElement updated = transform(current);
            await CommitAsync(key, path, updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            if (crossProcessLockHeld)
            {
                _crossProcessGate.Release();
            }
            if (processLockHeld)
            {
                _processWriteGate.Release();
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

    /// <summary>
    /// Serializes a complete JSON value to a unique temporary file and then replaces the logical
    /// state path. If a host/filesystem transient consumes the temporary file during replacement,
    /// rebuild the complete temp file and retry rather than exposing or accepting partial state.
    /// </summary>
    private async Task CommitAsync(string key, string path, JsonElement value, CancellationToken cancellationToken)
    {
        const int maxCommitAttempts = 3;
        IOException? lastIoError = null;

        for (int attempt = 1; attempt <= maxCommitAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_root);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (FileStream stream = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
                {
                    await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                MoveWithRetry(temp, path);
                return;
            }
            catch (IOException ex)
            {
                lastIoError = ex;
                if (attempt == maxCommitAttempts)
                {
                    break;
                }
            }
            finally
            {
                TryDelete(temp);
            }
        }

        throw new IOException(
            $"WinCare state '{key}' could not be committed atomically after {maxCommitAttempts} attempts.",
            lastIoError);
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

    private static void MoveWithRetry(string source, string destination)
    {
        const int maxAttempts = 25;
        for (int i = 1; i <= maxAttempts; i++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (FileNotFoundException)
            {
                // The destination may already contain an older valid state file, so its mere
                // existence cannot prove this commit succeeded. Rebuild the complete temp file
                // in CommitAsync and retry the intended value instead of accepting stale state.
                throw;
            }
            catch (Exception ex) when (i < maxAttempts && (ex is UnauthorizedAccessException or IOException))
            {
                Thread.Sleep(20);
            }
        }
    }
}
