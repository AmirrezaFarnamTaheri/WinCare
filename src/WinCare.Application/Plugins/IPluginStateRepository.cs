namespace WinCare.Application.Plugins;

using System.Collections.Generic;

public interface IPluginStateRepository
{
    HashSet<string> LoadEnabledPluginIds();
    void SaveEnabledPluginIds(IEnumerable<string> enabledPluginIds);
}
