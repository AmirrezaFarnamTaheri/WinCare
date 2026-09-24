using System.Text.Json;

namespace WinCare.Application.Commands;

/// <summary>
/// Durable, root-bounded key/value state surface available to catalog command handlers and to
/// extension packs. Implementations commit atomically and never let a caller escape the WinCare
/// data root.
/// </summary>
/// <remarks>
/// This is the seam a future durable backend (for example a SQLite WAL store) implements without
/// changing the command surface, and the boundary an extension pack programs against instead of
/// reaching into platform plumbing.
/// </remarks>
public interface ICommandStateStore
{
    /// <summary>
    /// Gets the absolute root directory every state key resolves under.
    /// </summary>
    string Root { get; }

    /// <summary>
    /// Reads one state key, or returns a clone of <paramref name="fallback" /> when it is absent.
    /// </summary>
    Task<JsonElement> ReadAsync(string key, JsonElement fallback, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces one state key wholesale inside the store's transaction lock.
    /// </summary>
    Task WriteAsync(string key, JsonElement value, CancellationToken cancellationToken);

    /// <summary>
    /// Reads, transforms, and writes one key inside a single transaction.
    /// </summary>
    Task<JsonElement> UpdateAsync(
        string key,
        JsonElement fallback,
        Func<JsonElement, JsonElement> transform,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads a key as a JSON object, falling back to an empty object.
    /// </summary>
    Task<JsonElement> ReadObjectAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a key as a JSON array, falling back to an empty array.
    /// </summary>
    Task<JsonElement> ReadArrayAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves an export path, defaulting to the user's Documents folder, expanding environment
    /// variables without escaping the allowed roots.
    /// </summary>
    string ResolveExportPath(string requestedPath, string defaultFileName);
}
