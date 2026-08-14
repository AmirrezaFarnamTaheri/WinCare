namespace WinCare.Application.Tests;

using System.Text.Json;
using WinCare.Application.Plugins;
using Xunit;

public sealed class PluginManifestTests
{
    [Fact]
    public void PluginManifest_Deserializes_Valid_Json_Schema()
    {
        // Arrange
        const string json = """
        {
          "id": "com.wincare.test.cleaner",
          "name": "Test Disk Cleaner",
          "version": "1.0.0",
          "author": "WinCare Devs",
          "description": "Test plugin description",
          "category": "System Care",
          "entryType": "Manifest",
          "tools": [
            {
              "id": "test.deep_clean",
              "title": "Deep Temp Purge",
              "summary": "Purges temporary files",
              "area": "System care",
              "section": "Storage",
              "risk": "Low",
              "readOnly": false,
              "executorType": "PowerShell",
              "scriptPath": "scripts/clean.ps1"
            }
          ]
        }
        """;

        // Act
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json);

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal("com.wincare.test.cleaner", manifest.Id);
        Assert.Equal("Test Disk Cleaner", manifest.Name);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal("WinCare Devs", manifest.Author);
        Assert.Equal("System Care", manifest.Category);
        Assert.Equal("Manifest", manifest.EntryType);
        Assert.Single(manifest.Tools);

        var tool = manifest.Tools[0];
        Assert.Equal("test.deep_clean", tool.Id);
        Assert.Equal("Deep Temp Purge", tool.Title);
        Assert.Equal("scripts/clean.ps1", tool.ScriptPath);

        var commandDef = tool.ToCommandDefinition(manifest.Id);
        Assert.Equal("test.deep_clean", commandDef.Id);
        Assert.Equal("Deep Temp Purge", commandDef.Title);
        Assert.Equal("System care", commandDef.Area);
    }
}
