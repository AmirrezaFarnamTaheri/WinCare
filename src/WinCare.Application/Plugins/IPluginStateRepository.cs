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
}
