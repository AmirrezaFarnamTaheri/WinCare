namespace WinCare.Application.Tests;

using System;
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
          "declaredCapabilities": ["filesystem.read", "filesystem.write"],
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
        Assert.Equal(2, manifest.DeclaredCapabilities.Count);
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

    [Fact]
    public void PluginManifest_FailsClosed_On_Invalid_Risk_Or_Admin_Value()
    {
        var invalidRiskTool = new PluginToolDefinition
        {
            Id = "test.bad_risk",
            Title = "Bad",
            Risk = "ExtremelyDangerous" // Invalid enum value
        };

        Assert.Throws<FormatException>(() => invalidRiskTool.ToCommandDefinition("test.plugin"));

        var invalidAdminTool = new PluginToolDefinition
        {
            Id = "test.bad_admin",
            Title = "Bad",
            Risk = "Low",
            AdministratorAccess = "RootUserOnly" // Invalid enum value
        };

        Assert.Throws<FormatException>(() => invalidAdminTool.ToCommandDefinition("test.plugin"));
    }
}
