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

    public CommandStateStore(string? root = null)
    {
        _root = Path.GetFullPath(root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinCare",
            "state"));
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

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

    public async Task WriteAsync(string key, JsonElement value, CancellationToken cancellationToken)
    {
        string path = PathFor(key);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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
            _gate.Release();
        }
    }

    public async Task<JsonElement> ReadObjectAsync(string key, CancellationToken cancellationToken) =>
        await ReadAsync(key, JsonSerializer.SerializeToElement(new Dictionary<string, object?>()), cancellationToken).ConfigureAwait(false);

    public async Task<JsonElement> ReadArrayAsync(string key, CancellationToken cancellationToken) =>
        await ReadAsync(key, JsonSerializer.SerializeToElement(Array.Empty<object>()), cancellationToken).ConfigureAwait(false);

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
