namespace WinCare.Infrastructure.Plugins;

using System.Text.Json;
using System.Text.Json.Serialization;
using WinCare.Application.Plugins;

/// <summary>
/// Serializable DTO for persisting enabled plugin IDs to JSON disk storage.
/// </summary>
public sealed class PluginStateFileModel
{
    /// <summary>
    /// List of enabled plugin IDs.
    /// </summary>
    [JsonPropertyName("enabledPluginIds")]
    public List<string> EnabledPluginIds { get; set; } = new();
}

/// <summary>
/// Persists plugin enabled/disabled state to local storage (%LocalAppData%/WinCare/plugins.json).
/// </summary>
public sealed class PluginStateRepository : IPluginStateRepository
{
    private readonly string _stateFilePath;

    /// <inheritdoc />
    public string? LastError { get; private set; }

    /// <summary>
    /// Initializes a new instance of <see cref="PluginStateRepository"/>.
    /// </summary>
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

    /// <inheritdoc />
    public HashSet<string> LoadEnabledPluginIds()
    {
        if (!File.Exists(_stateFilePath))
        {
            // A missing file indicates a fresh install, not an error; damaged or unreadable
            // data is reported distinctly through LastError instead of silently returning
            // an empty set.
            LastError = null;
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var json = File.ReadAllText(_stateFilePath);
            var model = JsonSerializer.Deserialize<PluginStateFileModel>(json);
            LastError = null;
            return new HashSet<string>(model?.EnabledPluginIds ?? new(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            LastError = $"Plugin state file '{_stateFilePath}' could not be read: {ex.Message} " +
                "The last known good file is left untouched on disk; plugin enablement falls back to disabled until it is repaired.";
            System.Diagnostics.Debug.WriteLine($"[PluginStateRepository] {LastError}");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <inheritdoc />
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
            var tempFilePath = _stateFilePath + ".tmp." + Guid.NewGuid().ToString("N");
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _stateFilePath, overwrite: true);
            LastError = null;
        }
        catch (Exception ex)
        {
            // Do not crash the host, but report that the save failed — the
            // write failure leaves the previous state file intact and the error is
            // recorded so callers can inform the user.
            LastError = $"Plugin state could not be saved to '{_stateFilePath}': {ex.Message} " +
                "The last known good state was retained; changes will be lost on restart.";
            System.Diagnostics.Debug.WriteLine($"[PluginStateRepository] {LastError}");
        }
    }
}
