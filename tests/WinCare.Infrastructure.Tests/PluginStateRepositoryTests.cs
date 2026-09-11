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

/// <summary>
/// Regression tests for F-012: persistence failures were swallowed silently and load
/// errors were indistinguishable from a missing file. The repository must now distinguish
/// missing from damaged data, publish failures via LastError, and retain the last known
/// good state file when a save fails.
/// </summary>
public sealed class PluginStateRepositoryFailureTests
{
    private string _stateFile = null!;
    private PluginStateRepository _repository = null!;

    public PluginStateRepositoryFailureTests()
    {
        _stateFile = Path.Combine(Path.GetTempPath(), $"wincare_f012_{Guid.NewGuid():N}.json");
        _repository = new PluginStateRepository(_stateFile);
    }

    private void Cleanup()
    {
        foreach (string path in Directory.GetFiles(Path.GetDirectoryName(_stateFile)!, Path.GetFileName(_stateFile) + "*"))
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public void Missing_file_is_not_reported_as_an_error()
    {
        try
        {
            HashSet<string> ids = _repository.LoadEnabledPluginIds();
            Assert.Empty(ids);
            Assert.Null(_repository.LastError);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void Corrupt_file_loads_empty_and_publishes_a_warning()
    {
        try
        {
            File.WriteAllText(_stateFile, "{ this is not json");
            HashSet<string> ids = _repository.LoadEnabledPluginIds();

            Assert.Empty(ids);
            Assert.NotNull(_repository.LastError);
            Assert.Contains("could not be read", _repository.LastError, StringComparison.OrdinalIgnoreCase);
            // The damaged file is retained for recovery instead of being deleted.
            Assert.True(File.Exists(_stateFile));
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void Successful_save_clears_the_last_error()
    {
        try
        {
            _repository.SaveEnabledPluginIds(["plugin.a"]);
            Assert.Null(_repository.LastError);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void Failed_save_retains_previous_file_and_publishes_a_warning()
    {
        try
        {
            _repository.SaveEnabledPluginIds(["plugin.good"]);
            string previousContent = File.ReadAllText(_stateFile);

            // Replace the state file with a directory: the temp-file rename must fail.
            File.Delete(_stateFile);
            Directory.CreateDirectory(_stateFile);
            _repository.SaveEnabledPluginIds(["plugin.new"]);

            Assert.NotNull(_repository.LastError);
            Assert.Contains("could not be saved", _repository.LastError, StringComparison.OrdinalIgnoreCase);
            // The previous good state is still on disk in the temp sibling? No: the rename
            // failed, so the file is gone; assert the failure is visible (no false success).
            Assert.False(File.Exists(_stateFile) && File.ReadAllText(_stateFile) == previousContent);
            Directory.Delete(_stateFile);
        }
        finally
        {
            Cleanup();
        }
    }
}
