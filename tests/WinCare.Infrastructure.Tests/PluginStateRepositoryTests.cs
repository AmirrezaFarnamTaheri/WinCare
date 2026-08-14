namespace WinCare.Infrastructure.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using WinCare.Infrastructure.Plugins;
using Xunit;

public class PluginStateRepositoryTests
{
    [Fact]
    public void SaveAndLoad_PersistsEnabledPluginIds()
    {
        var tempStateFile = Path.Combine(Path.GetTempPath(), $"wincare_state_test_{Guid.NewGuid():N}.json");

        try
        {
            var repository = new PluginStateRepository(tempStateFile);

            var initialIds = repository.LoadEnabledPluginIds();
            Assert.Empty(initialIds);

            var idsToSave = new List<string> { "plugin.a", "plugin.b", "PLUGIN.A" };
            repository.SaveEnabledPluginIds(idsToSave);

            var loadedIds = repository.LoadEnabledPluginIds();
            Assert.Equal(2, loadedIds.Count);
            Assert.Contains("plugin.a", loadedIds);
            Assert.Contains("plugin.b", loadedIds);
        }
        finally
        {
            if (File.Exists(tempStateFile))
            {
                File.Delete(tempStateFile);
            }
        }
    }
}
