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
}

public static class AppPreferences
{
    private static readonly object Sync = new();
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinCare");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");
    private static AppPreferenceData _current = Load();

    public static string Theme
    {
        get { lock (Sync) { return _current.Theme; } }
        set
        {
            string normalized = value is "Light" or "Dark" ? value : "System";
            lock (Sync)
            {
                if (string.Equals(_current.Theme, normalized, StringComparison.Ordinal)) return;
                _current = _current with { Theme = normalized };
                Save(_current);
            }
        }
    }

    /// <summary>Returns a snapshot of the favorited command IDs, newest last.</summary>
    public static IReadOnlyList<string> FavoriteCommandIds
    {
        get { lock (Sync) { return _current.FavoriteCommandIds.ToArray(); } }
    }

    /// <summary>Returns a snapshot of the recent command IDs, newest first.</summary>
    public static IReadOnlyList<string> RecentCommandIds
    {
        get { lock (Sync) { return _current.RecentCommandIds.ToArray(); } }
    }

    /// <summary>Persists the favorite command IDs.</summary>
    public static void SaveFavoriteCommandIds(IEnumerable<string> ids)
    {
        lock (Sync)
        {
            _current = _current with { FavoriteCommandIds = ids.Distinct().ToList() };
            Save(_current);
        }
    }

    /// <summary>Persists the recent command IDs.</summary>
    public static void SaveRecentCommandIds(IEnumerable<string> ids)
    {
        lock (Sync)
        {
            _current = _current with { RecentCommandIds = ids.ToList() };
            Save(_current);
        }
    }

    public static string DataDirectory => DirectoryPath;

    private static AppPreferenceData Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppPreferenceData>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    private static void Save(AppPreferenceData data)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Retain in-memory preference state even if disk persistence fails temporarily
        }
    }
}
