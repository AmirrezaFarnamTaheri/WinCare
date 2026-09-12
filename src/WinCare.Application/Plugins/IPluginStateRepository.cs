namespace WinCare.Application.Plugins;

using System.Collections.Generic;

/// <summary>
/// Repository interface for persisting enabled plugin state across application restarts.
/// </summary>
public interface IPluginStateRepository
{
    /// <summary>
    /// Loads the set of enabled plugin IDs from persistent storage.
    /// </summary>
    HashSet<string> LoadEnabledPluginIds();

    /// <summary>
    /// Saves the set of enabled plugin IDs to persistent storage.
    /// </summary>
    void SaveEnabledPluginIds(IEnumerable<string> enabledPluginIds);

    /// <summary>
    /// Describes the failure of the last persistence operation, or is null when the
    /// last load/save succeeded (a missing file is not an error). Callers should surface this
    /// instead of treating empty results as healthy persistence success.
    /// </summary>
    string? LastError { get; }
}
