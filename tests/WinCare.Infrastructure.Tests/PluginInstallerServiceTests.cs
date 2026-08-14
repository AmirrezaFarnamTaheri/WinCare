using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
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
            var installedPath = await installer.InstallPluginFromStreamAsync(memoryStream, "com.wincare.customplugin");

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
    public async Task InstallPluginFromStreamAsync_RejectsManifestIdMismatch()
    {
        var tempPluginsDir = Path.Combine(Path.GetTempPath(), $"wincare_test_plugins_{Guid.NewGuid():N}");

        try
        {
            using var memoryStream = new MemoryStream();
            using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifestEntry = zip.CreateEntry("wincare-plugin.json");
                using var writer = new StreamWriter(manifestEntry.Open());
                writer.Write(JsonSerializer.Serialize(new { id = "actual.plugin.id", name = "Actual Plugin", version = "1.0.0" }));
            }

            memoryStream.Position = 0;
            var installer = new PluginInstallerService(pluginsBaseDirectory: tempPluginsDir);

            // Attempt to install archive whose manifest says 'actual.plugin.id' with expected target 'expected.plugin.id'
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                installer.InstallPluginFromStreamAsync(memoryStream, "expected.plugin.id"));
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
    public async Task InstallPluginFromStreamAsync_EnforcesStreamSha256Digest()
    {
        var tempPluginsDir = Path.Combine(Path.GetTempPath(), $"wincare_test_plugins_{Guid.NewGuid():N}");

        try
        {
            using var memoryStream = new MemoryStream();
            using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifestEntry = zip.CreateEntry("wincare-plugin.json");
                using var writer = new StreamWriter(manifestEntry.Open());
                writer.Write(JsonSerializer.Serialize(new { id = "hash.test.plugin", name = "Hash Test", version = "1.0.0" }));
            }

            byte[] bytes = memoryStream.ToArray();
            string validHash = Convert.ToHexString(SHA256.HashData(bytes));
            string invalidHash = "0000000000000000000000000000000000000000000000000000000000000000";

            var installer = new PluginInstallerService(pluginsBaseDirectory: tempPluginsDir);

            // 1. Mismatched SHA-256 throws InvalidOperationException
            memoryStream.Position = 0;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                installer.InstallPluginFromStreamAsync(memoryStream, "hash.test.plugin", invalidHash));

            // 2. Correct SHA-256 succeeds
            memoryStream.Position = 0;
            var installedPath = await installer.InstallPluginFromStreamAsync(memoryStream, "hash.test.plugin", validHash);
            Assert.True(Directory.Exists(installedPath));
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

    [Theory]
    [InlineData("http://example.com/plugin.zip")]
    [InlineData("ftp://example.com/plugin.zip")]
    public async Task InstallPluginFromPackageAsync_RejectsNonHttpsUrls(string url)
    {
        var tempPluginsDir = Path.Combine(Path.GetTempPath(), $"wincare_test_plugins_{Guid.NewGuid():N}");
        try
        {
            var installer = new PluginInstallerService(pluginsBaseDirectory: tempPluginsDir);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                installer.InstallPluginFromPackageAsync(url));
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
    public async Task InstallPluginFromStreamAsync_RejectsMissingManifest()
    {
        var tempPluginsDir = Path.Combine(Path.GetTempPath(), $"wincare_test_plugins_{Guid.NewGuid():N}");

        try
        {
            using var memoryStream = new MemoryStream();
            using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = zip.CreateEntry("somefile.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("no manifest here");
            }

            memoryStream.Position = 0;
            var installer = new PluginInstallerService(pluginsBaseDirectory: tempPluginsDir);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                installer.InstallPluginFromStreamAsync(memoryStream, "com.wincare.nomanifest"));
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
    public void VerifyPublisherAuthenticity_Identifies_Known_Organizations_And_Signed_Packages()
    {
        Assert.True(PluginInstallerService.VerifyPublisherAuthenticity("WinCare Official", null, out var officialTrust));
        Assert.Equal("Verified Organization", officialTrust);

        Assert.True(PluginInstallerService.VerifyPublisherAuthenticity("WinCare Community", null, out var commTrust));
        Assert.Equal("Verified Organization", commTrust);

        Assert.True(PluginInstallerService.VerifyPublisherAuthenticity("ThirdParty Developer", "signature_hash_data", out var signedTrust));
        Assert.Equal("Digitally Signed", signedTrust);

        Assert.False(PluginInstallerService.VerifyPublisherAuthenticity("Unknown Developer", null, out var unsignedTrust));
        Assert.Equal("Community / Unsigned", unsignedTrust);
    }

    [Fact]
    public async Task InstallPluginFromPackageAsync_Installs_From_File_Uri_Successfully()
    {
        var tempPluginsDir = Path.Combine(Path.GetTempPath(), $"wincare_test_plugins_{Guid.NewGuid():N}");
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"wincare_test_pkg_{Guid.NewGuid():N}.zip");

        try
        {
            using (var zip = ZipFile.Open(tempZipPath, ZipArchiveMode.Create))
            {
                var manifestEntry = zip.CreateEntry("wincare-plugin.json");
                using var writer = new StreamWriter(manifestEntry.Open());
                writer.Write(JsonSerializer.Serialize(new { id = "com.wincare.fileuri.test", name = "File URI Plugin", version = "1.0.0" }));
            }

            var installer = new PluginInstallerService(pluginsBaseDirectory: tempPluginsDir);
            var fileUri = new Uri(tempZipPath).AbsoluteUri;

            var installedPath = await installer.InstallPluginFromPackageAsync(fileUri, "com.wincare.fileuri.test");

            Assert.True(Directory.Exists(installedPath));
            Assert.True(File.Exists(Path.Combine(installedPath, "wincare-plugin.json")));
        }
        finally
        {
            if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
            if (Directory.Exists(tempPluginsDir)) Directory.Delete(tempPluginsDir, recursive: true);
        }
    }
}
