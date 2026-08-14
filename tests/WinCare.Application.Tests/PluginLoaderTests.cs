namespace WinCare.Application.Tests;

using WinCare.Application.Plugins;
using Xunit;

public sealed class PluginLoaderTests
{
    [Fact]
    public void JsonPluginLoader_Loads_Valid_Directory()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "WinCarePluginTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var scriptsDir = Path.Combine(tempDir, "scripts");
        Directory.CreateDirectory(scriptsDir);

        var scriptFile = Path.Combine(scriptsDir, "clean.ps1");
        File.WriteAllText(scriptFile, "Write-Host 'Cleaning...'");

        var manifestJson = """
        {
          "id": "com.wincare.test.loader",
          "name": "Loader Test Plugin",
          "version": "1.0.0",
          "author": "WinCare Test",
          "tools": [
            {
              "id": "loader.test_clean",
              "title": "Test Clean",
              "summary": "Test tool",
              "area": "System care",
              "section": "Storage",
              "scriptPath": "scripts/clean.ps1"
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(tempDir, "wincare-plugin.json"), manifestJson);

        try
        {
            // Act
            var result = JsonPluginLoader.LoadFromDirectory(tempDir);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Manifest);
            Assert.Equal("com.wincare.test.loader", result.Manifest.Id);
            Assert.Single(result.Commands);
            Assert.Equal("loader.test_clean", result.Commands[0].Id);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void JsonPluginLoader_Rejects_Path_Traversal_Security_Violation()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "WinCareSecurityTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var manifestJson = """
        {
          "id": "com.wincare.security.violation",
          "name": "Malicious Plugin",
          "version": "1.0.0",
          "tools": [
            {
              "id": "malicious.hack",
              "title": "Path Traversal Tool",
              "summary": "Tries to break root boundary",
              "area": "System care",
              "section": "Storage",
              "scriptPath": "../../../Windows/System32/cmd.exe"
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(tempDir, "wincare-plugin.json"), manifestJson);

        try
        {
            // Act
            var result = JsonPluginLoader.LoadFromDirectory(tempDir);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Security Violation", result.ErrorMessage);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
