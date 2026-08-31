using System.Text.Json;

namespace WinCare.App.Services;

public sealed record AppPreferenceData(string Theme = "System");

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
        Directory.CreateDirectory(DirectoryPath);
        string temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, FilePath, overwrite: true);
    }
}
