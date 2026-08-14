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
    public void VerifyManifestSignature_Validates_Cryptographic_RSA_Signatures()
    {
        using var rsa = RSA.Create(2048);
        var publicKeyPem = rsa.ExportRSAPublicKeyPem();
        var manifestContent = System.Text.Encoding.UTF8.GetBytes("{\"id\":\"com.signed.tool\",\"version\":\"1.0.0\"}");
        var signatureBytes = rsa.SignData(manifestContent, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var signatureBase64 = Convert.ToBase64String(signatureBytes);

        // 1. Valid signature check
        var isValid = PluginInstallerService.VerifyManifestSignature(manifestContent, signatureBase64, publicKeyPem);
        Assert.True(isValid);

        // 2. Full publisher authenticity check with cryptographic verification
        Assert.True(PluginInstallerService.VerifyPublisherAuthenticity("Independent Developer", signatureBase64, out var trustLevel, publicKeyPem, manifestContent));
        Assert.Equal("Digitally Signed (Cryptographically Verified)", trustLevel);

        // 3. Tampered content verification check
        var tamperedContent = System.Text.Encoding.UTF8.GetBytes("{\"id\":\"com.signed.tool\",\"version\":\"2.0.0-tampered\"}");
        var isTamperedValid = PluginInstallerService.VerifyManifestSignature(tamperedContent, signatureBase64, publicKeyPem);
        Assert.False(isTamperedValid);
        Assert.False(PluginInstallerService.VerifyPublisherAuthenticity("Independent Developer", signatureBase64, out var tamperedTrust, publicKeyPem, tamperedContent));
        Assert.Equal("Signature Verification Failed", tamperedTrust);
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

    [Fact]
    public void VerifyManifestSignature_Rejects_Wrong_Publisher_Key()
    {
        using var rsaPublisherA = RSA.Create(2048);
        using var rsaPublisherB = RSA.Create(2048);

        var manifestContent = System.Text.Encoding.UTF8.GetBytes("{\"id\":\"com.adversarial.test\",\"version\":\"1.0.0\"}");
        var signatureBytes = rsaPublisherA.SignData(manifestContent, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var signatureBase64 = Convert.ToBase64String(signatureBytes);

        var wrongPublicKeyPem = rsaPublisherB.ExportRSAPublicKeyPem();

        // Verification against wrong publisher public key must fail closed
        var isValid = PluginInstallerService.VerifyManifestSignature(manifestContent, signatureBase64, wrongPublicKeyPem);
        Assert.False(isValid);

        Assert.False(PluginInstallerService.VerifyPublisherAuthenticity("Untrusted Publisher", signatureBase64, out var trustLevel, wrongPublicKeyPem, manifestContent));
        Assert.Equal("Signature Verification Failed", trustLevel);
    }

    [Fact]
    public void VerifyManifestSignature_Handles_Corrupted_Payloads_Gracefully()
    {
        var manifestContent = System.Text.Encoding.UTF8.GetBytes("{\"id\":\"com.adversarial.corrupted\"}");

        Assert.False(PluginInstallerService.VerifyManifestSignature(manifestContent, "not-valid-base64!!!", "invalid-pem"));
        Assert.False(PluginInstallerService.VerifyManifestSignature(Array.Empty<byte>(), "validBase64==", "validPem"));
        Assert.False(PluginInstallerService.VerifyManifestSignature(manifestContent, string.Empty, string.Empty));
    }

    [Fact]
    public async Task InstallPluginFromStreamAsync_Rejects_Package_With_Failed_Digital_Signature()
    {
        var tempPluginsDir = Path.Combine(Path.GetTempPath(), $"wincare_test_plugins_{Guid.NewGuid():N}");

        try
        {
            using var rsa = RSA.Create(2048);
            var publicKeyPem = rsa.ExportRSAPublicKeyPem();

            using var memoryStream = new MemoryStream();
            using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifestEntry = zip.CreateEntry("wincare-plugin.json");
                using (var writer = new StreamWriter(manifestEntry.Open()))
                {
                    writer.Write(JsonSerializer.Serialize(new 
                    { 
                        id = "com.wincare.forged.plugin", 
                        name = "Forged Signature Plugin", 
                        version = "1.0.0",
                        signature = "Zm9yZ2VkLXNpZ25hdHVyZQ==", // forged base64 signature
                        publicKey = publicKeyPem
                    }));
                }
            }

            memoryStream.Position = 0;
            var installer = new PluginInstallerService(pluginsBaseDirectory: tempPluginsDir);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                installer.InstallPluginFromStreamAsync(memoryStream, "com.wincare.forged.plugin"));

            Assert.Contains("Digital signature verification failed", ex.Message);
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
    public async Task InstallPluginFromStreamAsync_Promotes_Update_And_Cleans_Staging()
    {
        var tempPluginsDir = Path.Combine(Path.GetTempPath(), $"wincare_test_plugins_{Guid.NewGuid():N}");

        try
        {
            var installer = new PluginInstallerService(pluginsBaseDirectory: tempPluginsDir);

            // 1. Initial installation of v1.0.0
            using (var msV1 = new MemoryStream())
            {
                using (var zip1 = new ZipArchive(msV1, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var mEntry = zip1.CreateEntry("wincare-plugin.json");
                    using var writer = new StreamWriter(mEntry.Open());
                    writer.Write(JsonSerializer.Serialize(new { id = "com.wincare.upgradeable", name = "Upgradeable Plugin", version = "1.0.0" }));
                }

                msV1.Position = 0;
                await installer.InstallPluginFromStreamAsync(msV1, "com.wincare.upgradeable");
            }

            var pluginDir = Path.Combine(tempPluginsDir, "com.wincare.upgradeable");
            Assert.True(Directory.Exists(pluginDir));
            var v1Manifest = await File.ReadAllTextAsync(Path.Combine(pluginDir, "wincare-plugin.json"));
            Assert.Contains("\"1.0.0\"", v1Manifest);

            // 2. Install v2.0.0 update over existing directory
            using (var msV2 = new MemoryStream())
            {
                using (var zip2 = new ZipArchive(msV2, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var mEntry = zip2.CreateEntry("wincare-plugin.json");
                    using var writer = new StreamWriter(mEntry.Open());
                    writer.Write(JsonSerializer.Serialize(new { id = "com.wincare.upgradeable", name = "Upgradeable Plugin", version = "2.0.0" }));
                }

                msV2.Position = 0;
                var updatedPath = await installer.InstallPluginFromStreamAsync(msV2, "com.wincare.upgradeable");
                Assert.Equal(pluginDir, updatedPath);
            }

            // Verify updated manifest content was promoted
            var v2Manifest = await File.ReadAllTextAsync(Path.Combine(pluginDir, "wincare-plugin.json"));
            Assert.Contains("\"2.0.0\"", v2Manifest);

            // Verify temporary staging extraction folders were cleaned up
            var stagingDir = Path.Combine(tempPluginsDir, ".staging");
            if (Directory.Exists(stagingDir))
            {
                var extractFolders = Directory.GetDirectories(stagingDir, "install_*");
                Assert.Empty(extractFolders);
            }
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
    public void TelemetryEvidence_Freshness_Validation_Detects_Stale_Metrics()
    {
        var freshEvidence = new WinCare.Application.Diagnostics.TelemetryEvidence(
            HasMeasuredEvidence: true,
            MetricName: "Fresh Metric",
            MeasuredValue: "Optimal",
            IndicatesPressure: false,
            Severity: WinCare.Application.Diagnostics.DiagnosticSeverity.Healthy,
            CapturedAtUtc: DateTime.UtcNow
        );

        Assert.False(freshEvidence.IsStale(TimeSpan.FromMinutes(1)));

        var staleEvidence = new WinCare.Application.Diagnostics.TelemetryEvidence(
            HasMeasuredEvidence: true,
            MetricName: "Stale Metric",
            MeasuredValue: "Low Storage",
            IndicatesPressure: true,
            Severity: WinCare.Application.Diagnostics.DiagnosticSeverity.Warning,
            CapturedAtUtc: DateTime.UtcNow.AddMinutes(-10)
        );

        Assert.True(staleEvidence.IsStale(TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void VerifyManifestSignature_Rejects_Modified_Signed_Package()
    {
        using var rsa = RSA.Create(2048);
        var publicKeyPem = rsa.ExportRSAPublicKeyPem();

        var originalManifest = "{\"id\":\"com.wincare.test\",\"name\":\"Original Test\",\"version\":\"1.0.0\"}";
        var originalBytes = Encoding.UTF8.GetBytes(originalManifest);
        var signatureBytes = rsa.SignData(originalBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var signatureBase64 = Convert.ToBase64String(signatureBytes);

        // Tamper with manifest content after signing
        var tamperedManifest = "{\"id\":\"com.wincare.test\",\"name\":\"Tampered Test\",\"version\":\"1.0.0\"}";
        var tamperedBytes = Encoding.UTF8.GetBytes(tamperedManifest);

        var installer = new PluginInstallerService();
        var isValid = installer.VerifyManifestSignature(tamperedBytes, signatureBase64, publicKeyPem, out var error);

        Assert.False(isValid);
        Assert.Contains("Invalid signature", error);
    }

    [Fact]
    public void TelemetryEvidence_Provenance_Attributes_Are_Populated_And_Formatted()
    {
        var evidence = new WinCare.Application.Diagnostics.TelemetryEvidence(
            HasMeasuredEvidence: true,
            MetricName: "System RAM Usage",
            MeasuredValue: "92% utilized",
            IndicatesPressure: true,
            Severity: WinCare.Application.Diagnostics.DiagnosticSeverity.Warning,
            Source: "Windows Memory Subsystem Telemetry",
            CommandId: "wincare.systemcare.ramoptimizer",
            Collector: "MemoryDiagnosticsCollector",
            CommandVersion: "1.1.0",
            CapturedAtUtc: new DateTime(2026, 8, 15, 2, 30, 0, DateTimeKind.Utc)
        );

        Assert.Equal("MemoryDiagnosticsCollector", evidence.Collector);
        Assert.Equal("1.1.0", evidence.CommandVersion);
        Assert.Equal("wincare.systemcare.ramoptimizer", evidence.CommandId);
        Assert.Contains("MemoryDiagnosticsCollector", evidence.ProvenanceSummary);
        Assert.Contains("wincare.systemcare.ramoptimizer@1.1.0", evidence.ProvenanceSummary);
    }
}
