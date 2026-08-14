namespace WinCare.Infrastructure.Plugins;

using System.Text.Json;
using System.Text.Json.Serialization;
using WinCare.Application.Plugins;

public sealed class PluginStateFileModel
{
    [JsonPropertyName("enabledPluginIds")]
    public List<string> EnabledPluginIds { get; set; } = new();
}

/// <summary>
/// Persists plugin enabled/disabled state to local storage (%LocalAppData%/WinCare/plugins.json).
/// </summary>
public sealed class PluginStateRepository
{
    private readonly string _stateFilePath;

    public PluginStateRepository(string? customStateFilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(customStateFilePath))
        {
            _stateFilePath = customStateFilePath;
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var wincareDir = Path.Combine(localAppData, "WinCare");
            Directory.CreateDirectory(wincareDir);
            _stateFilePath = Path.Combine(wincareDir, "plugins.json");
        }
    }

    public HashSet<string> LoadEnabledPluginIds()
    {
        if (!File.Exists(_stateFilePath))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = File.ReadAllText(_stateFilePath);
            var model = JsonSerializer.Deserialize<PluginStateFileModel>(json);
            return new HashSet<string>(model?.EnabledPluginIds ?? new(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SaveEnabledPluginIds(IEnumerable<string> enabledPluginIds)
    {
        try
        {
            var directory = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var model = new PluginStateFileModel
            {
                EnabledPluginIds = enabledPluginIds.Distinct().ToList()
            };

            var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_stateFilePath, json);
        }
        catch
        {
            // Ignore storage write errors to prevent host crash
        }
    }
}
