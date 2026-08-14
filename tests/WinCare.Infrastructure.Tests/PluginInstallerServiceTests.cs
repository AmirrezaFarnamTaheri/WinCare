using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using WinCare.Infrastructure.Plugins;
using Xunit;

namespace WinCare.Infrastructure.Tests;

public class PluginInstallerServiceTests
{
    [Fact]
    public async Task InstallPluginFromStreamAsync_ExtractsArchiveAndReadsManifest()
    {
        var tempPluginsDir = Path.Combine(Path.GetTempPath(), $"wincare_test_plugins_{Guid.NewGuid():N}");

        try
        {
            using var memoryStream = new MemoryStream();
            using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifestEntry = zip.CreateEntry("plugin.json");
                using (var writer = new StreamWriter(manifestEntry.Open()))
                {
                    writer.Write(JsonSerializer.Serialize(new { id = "com.wincare.customplugin", name = "Custom Plugin", version = "1.0.0" }));
                }

                var codeEntry = zip.CreateEntry("CustomPlugin.dll");
                using (var writer = new StreamWriter(codeEntry.Open()))
                {
                    writer.Write("dummy binary content");
                }
            }

            memoryStream.Position = 0;

            var installer = new PluginInstallerService(pluginsBaseDirectory: tempPluginsDir);
            var installedPath = await installer.InstallPluginFromStreamAsync(memoryStream, "fallback_id");

            Assert.True(Directory.Exists(installedPath));
            Assert.EndsWith("com.wincare.customplugin", installedPath);
            Assert.True(File.Exists(Path.Combine(installedPath, "plugin.json")));
            Assert.True(File.Exists(Path.Combine(installedPath, "CustomPlugin.dll")));
        }
        finally
        {
            if (Directory.Exists(tempPluginsDir))
            {
                Directory.Delete(tempPluginsDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InstallPluginFromStreamAsync_RejectsPathTraversalInZip()
    {
        var tempPluginsDir = Path.Combine(Path.GetTempPath(), $"wincare_test_plugins_{Guid.NewGuid():N}");

        try
        {
            using var memoryStream = new MemoryStream();
            using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var maliciousEntry = zip.CreateEntry("../../../malicious.txt");
                using (var writer = new StreamWriter(maliciousEntry.Open()))
                {
                    writer.Write("exploit payload");
                }
            }

            memoryStream.Position = 0;

            var installer = new PluginInstallerService(pluginsBaseDirectory: tempPluginsDir);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                installer.InstallPluginFromStreamAsync(memoryStream, "malicious_plugin"));
        }
        finally
        {
            if (Directory.Exists(tempPluginsDir))
            {
                Directory.Delete(tempPluginsDir, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("../malicious")]
    [InlineData("..\\malicious")]
    [InlineData("C:\\Windows\\System32")]
    [InlineData("inv@lid*id!")]
    [InlineData("a")]
    [InlineData("")]
    public async Task InstallPluginFromStreamAsync_RejectsInvalidPluginId(string invalidId)
    {
        var tempPluginsDir = Path.Combine(Path.GetTempPath(), $"wincare_test_plugins_{Guid.NewGuid():N}");

        try
        {
            using var memoryStream = new MemoryStream();
            using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifestEntry = zip.CreateEntry("plugin.json");
                using (var writer = new StreamWriter(manifestEntry.Open()))
                {
                    writer.Write(JsonSerializer.Serialize(new { id = invalidId, name = "Invalid Plugin", version = "1.0.0" }));
                }
            }

            memoryStream.Position = 0;
            var installer = new PluginInstallerService(pluginsBaseDirectory: tempPluginsDir);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                installer.InstallPluginFromStreamAsync(memoryStream, invalidId));
        }
        finally
        {
            if (Directory.Exists(tempPluginsDir))
            {
                Directory.Delete(tempPluginsDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UninstallPluginAsync_SafelyRemovesPluginDirectory()
    {
        var tempPluginsDir = Path.Combine(Path.GetTempPath(), $"wincare_test_plugins_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPluginsDir);

        var pluginDir = Path.Combine(tempPluginsDir, "com.wincare.testuninstall");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "plugin.json"), "{}");

        try
        {
            var installer = new PluginInstallerService(pluginsBaseDirectory: tempPluginsDir);
            var result = await installer.UninstallPluginAsync("com.wincare.testuninstall");

            Assert.True(result);
            Assert.False(Directory.Exists(pluginDir));
        }
        finally
        {
            if (Directory.Exists(tempPluginsDir))
            {
                Directory.Delete(tempPluginsDir, recursive: true);
            }
        }
    }
}
