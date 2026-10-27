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
    [Fact]
    public void Admitted_script_bytes_are_bound_to_external_trust_record()
    {
        string root = Path.Combine(Path.GetTempPath(), "WinCarePluginBinding_" + Guid.NewGuid().ToString("N"));
        string directory = Path.Combine(root, "plugins", "demo");
        Directory.CreateDirectory(directory);
        try
        {
            string script = Path.Combine(directory, "job.ps1");
            File.WriteAllText(script, "Write-Output safe");
            string manifest = """
                {"id":"com.wincare.payload.demo","name":"Demo","version":"1.0",
                 "tools":[{"id":"demo.run","title":"Run","summary":"Run job","scriptPath":"job.ps1"}]}
                """;
            File.WriteAllText(Path.Combine(directory, "wincare-plugin.json"), manifest);

            string recordPath = PluginAdmissionTrustStore.GetUserScopedRecordPath(directory);
            Directory.CreateDirectory(Path.GetDirectoryName(recordPath)!);
            var record = new PluginAdmissionRecord
            {
                PluginId = "com.wincare.payload.demo",
                ManifestSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant(),
                PackageTreeSha256 = PluginAdmissionTrustStore.ComputePluginPayloadDigest(directory),
                TrustScope = PluginAdmissionTrustStore.UserTrustScope,
            };
            File.WriteAllText(recordPath, System.Text.Json.JsonSerializer.Serialize(record));

            Assert.True(JsonPluginLoader.LoadFromDirectory(directory, true, static () => false).Success);
            File.WriteAllText(script, "Write-Output replaced");

            PluginLoadResult afterTampering = JsonPluginLoader.LoadFromDirectory(directory, true, static () => false);
            Assert.False(afterTampering.Success);
            Assert.Contains("payload", afterTampering.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

}
