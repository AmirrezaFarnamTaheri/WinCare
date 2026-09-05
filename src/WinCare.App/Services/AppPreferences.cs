using System.Text.Json;

namespace WinCare.App.Services;

/// <summary>
/// Persisted per-user preference state, including the theme and the favorite/recent tool
/// lists that back the All Tools page across restarts.
/// </summary>
public sealed record AppPreferenceData(string Theme = "System")
{
    /// <summary>Stable command IDs the user has favorited.</summary>
    public List<string> FavoriteCommandIds { get; init; } = new();

    /// <summary>Most-recently-run command IDs, newest first.</summary>
    public List<string> RecentCommandIds { get; init; } = new();

    /// <summary>Normalizes persisted data before exposing it to page bindings.</summary>
    public AppPreferenceData Normalize() => this with
    {
        Theme = Theme is "Light" or "Dark" ? Theme : "System",
        FavoriteCommandIds = (FavoriteCommandIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(512).ToList(),
        RecentCommandIds = (RecentCommandIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList(),
    };
}

public static class AppPreferences
{
    private static readonly object StateSync = new();
    private static readonly object PersistenceSync = new();
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinCare");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");
    private static string? _persistenceStatusMessage;
    private static AppPreferenceData _current = Load(out _persistenceStatusMessage);
    private static Task _persistenceTail = Task.CompletedTask;

    public static event EventHandler? PersistenceStatusChanged;

    public static string Theme
    {
        get { lock (StateSync) { return _current.Theme; } }
        set
        {
            string normalized = value is "Light" or "Dark" ? value : "System";
            AppPreferenceData? snapshot = null;
            lock (StateSync)
            {
                if (string.Equals(_current.Theme, normalized, StringComparison.Ordinal)) return;
                _current = _current with { Theme = normalized };
                snapshot = Snapshot();
            }
            QueueSave(snapshot);
        }
    }

    /// <summary>Returns a snapshot of the favorited command IDs, newest last.</summary>
    public static IReadOnlyList<string> FavoriteCommandIds
    {
        get { lock (StateSync) { return _current.FavoriteCommandIds.ToArray(); } }
    }

    /// <summary>Returns a snapshot of the recent command IDs, newest first.</summary>
    public static IReadOnlyList<string> RecentCommandIds
    {
        get { lock (StateSync) { return _current.RecentCommandIds.ToArray(); } }
    }

    public static bool IsPersistenceHealthy
    {
        get
        {
            lock (PersistenceSync)
            {
                return string.IsNullOrWhiteSpace(_persistenceStatusMessage);
            }
        }
    }

    public static string? PersistenceStatusMessage
    {
        get
        {
            lock (PersistenceSync)
            {
                return _persistenceStatusMessage;
            }
        }
    }

    /// <summary>Persists the favorite command IDs without blocking the calling UI thread on disk I/O.</summary>
    public static void SaveFavoriteCommandIds(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        AppPreferenceData snapshot;
        lock (StateSync)
        {
            _current = (_current with { FavoriteCommandIds = ids.ToList() }).Normalize();
            snapshot = Snapshot();
        }
        QueueSave(snapshot);
    }

    /// <summary>Persists the recent command IDs without blocking the calling UI thread on disk I/O.</summary>
    public static void SaveRecentCommandIds(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        AppPreferenceData snapshot;
        lock (StateSync)
        {
            _current = (_current with { RecentCommandIds = ids.ToList() }).Normalize();
            snapshot = Snapshot();
        }
        QueueSave(snapshot);
    }

    public static string DataDirectory => DirectoryPath;

    public static async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Task pending;
        lock (PersistenceSync)
        {
            pending = _persistenceTail;
        }
        await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AppPreferenceData Snapshot() => _current with
    {
        FavoriteCommandIds = _current.FavoriteCommandIds.ToList(),
        RecentCommandIds = _current.RecentCommandIds.ToList(),
    };

    private static AppPreferenceData Load(out string? persistenceStatusMessage)
    {
        persistenceStatusMessage = null;
        try
        {
            if (!File.Exists(FilePath))
            {
                return new();
            }
            if (new FileInfo(FilePath).Length > 1024 * 1024)
            {
                persistenceStatusMessage = "Saved preferences exceeded the safety limit and were not loaded. New preferences may still be used for this session.";
                return new();
            }

            return (JsonSerializer.Deserialize<AppPreferenceData>(File.ReadAllText(FilePath)) ?? new()).Normalize();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            persistenceStatusMessage = "Saved preferences could not be loaded. WinCare is using session defaults until preferences can be saved again.";
            System.Diagnostics.Debug.WriteLine($"[AppPreferences] Load failed: {ex}");
            return new();
        }
    }

    private static void QueueSave(AppPreferenceData data)
    {
        lock (PersistenceSync)
        {
            _persistenceTail = _persistenceTail.ContinueWith(
                _ => SaveAsync(data),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
        }
    }

    private static async Task SaveAsync(AppPreferenceData data)
    {
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            temporaryPath = FilePath + ".tmp." + Guid.NewGuid().ToString("N");
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(temporaryPath, json).ConfigureAwait(false);
            File.Move(temporaryPath, FilePath, overwrite: true);
            temporaryPath = null;
            SetPersistenceStatus(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            SetPersistenceStatus("WinCare is using your updated preferences in memory, but cannot save them to disk. They may be lost when the app exits.");
            System.Diagnostics.Debug.WriteLine($"[AppPreferences] Save failed: {ex}");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryPath))
            {
                try { File.Delete(temporaryPath); } catch { }
            }
        }
    }

    private static void SetPersistenceStatus(string? message)
    {
        bool changed;
        lock (PersistenceSync)
        {
            changed = !string.Equals(_persistenceStatusMessage, message, StringComparison.Ordinal);
            _persistenceStatusMessage = message;
        }
        if (changed)
        {
            try { PersistenceStatusChanged?.Invoke(null, EventArgs.Empty); } catch { }
        }
    }
}
