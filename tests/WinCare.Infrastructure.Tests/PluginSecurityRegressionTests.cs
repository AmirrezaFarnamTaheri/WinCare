using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using WinCare.Application.Commands;
using WinCare.Application.Plugins;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Plugins;
using Xunit;

namespace WinCare.Infrastructure.Tests;

public sealed class PluginSecurityRegressionTests
{
    [Fact]
    public void RemotePluginInstallPolicy_RejectsDelistedAndChangedReviewedEntries()
    {
        var reviewed = CreateRemoteItem();

        var delisted = new RemotePluginCatalog { IsTrustVerified = true };
        var delistedError = Assert.Throws<InvalidOperationException>(() =>
            RemotePluginInstallPolicy.ResolveFreshReviewedEntry(delisted, reviewed, reviewed.Permissions));
        Assert.Contains("no longer present", delistedError.Message, StringComparison.OrdinalIgnoreCase);

        var changed = CreateRemoteItem();
        changed.Sha256 = new string('b', 64);
        var changedCatalog = new RemotePluginCatalog { IsTrustVerified = true, Plugins = new List<RemotePluginItem> { changed } };
        var changedError = Assert.Throws<InvalidOperationException>(() =>
            RemotePluginInstallPolicy.ResolveFreshReviewedEntry(changedCatalog, reviewed, reviewed.Permissions));
        Assert.Contains("changed", changedError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemotePluginInstallPolicy_RejectsCatalogWithoutPinnedTrust()
    {
        var reviewed = CreateRemoteItem();
        var untrustedCatalog = new RemotePluginCatalog
        {
            Plugins = new List<RemotePluginItem> { CreateRemoteItem() }
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            RemotePluginInstallPolicy.ResolveFreshReviewedEntry(untrustedCatalog, reviewed, reviewed.Permissions));

        Assert.Contains("pinned trust root", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoteCatalogService_VerifiesDetachedCatalogSignatureAgainstPinnedKey()
    {
        var root = Path.Combine(Path.GetTempPath(), "WinCareCatalogSignature_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var payloadCatalog = new RemotePluginCatalog
            {
                CatalogVersion = "1.0",
                Plugins = new List<RemotePluginItem> { CreateRemoteItem() }
            };
            string json = JsonSerializer.Serialize(payloadCatalog);
            byte[] payload = Encoding.UTF8.GetBytes(json);
            using var rsa = RSA.Create(2048);
            string publicKeyPem = rsa.ExportRSAPublicKeyPem();
            string signature = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

            using var httpClient = new HttpClient(new CatalogSignatureHttpHandler(json, signature));
            var service = new RemoteCatalogService(
                httpClient,
                cacheFilePath: Path.Combine(root, "catalog.json"),
                catalogUrl: "https://catalog.invalid/catalog.json",
                trustedCatalogPublicKeyPem: publicKeyPem,
                catalogSignatureUrl: "https://catalog.invalid/catalog.json.sig");

            RemotePluginCatalog catalog = await service.GetCatalogAsync(forceRefresh: true);

            Assert.True(catalog.IsTrustVerified);
            Assert.All(catalog.Plugins, item => Assert.True(item.IsCatalogTrustVerified));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RemoteCatalogService_ForcedRefresh_DoesNotFallBackToStaleCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "WinCareCatalogFreshness_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var cachePath = Path.Combine(root, "catalog.json");

        try
        {
            var staleCatalog = new RemotePluginCatalog
            {
                LastUpdated = DateTime.UtcNow.AddDays(-30),
                Plugins = new List<RemotePluginItem> { CreateRemoteItem() }
            };
            File.WriteAllText(cachePath, JsonSerializer.Serialize(staleCatalog));
            File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow.AddDays(-30));

            using var httpClient = new HttpClient(new StaticHttpHandler(HttpStatusCode.ServiceUnavailable, "offline"));
            var service = new RemoteCatalogService(
                httpClient,
                cacheFilePath: cachePath,
                catalogUrl: "https://catalog.invalid/catalog.json",
                cacheDuration: TimeSpan.FromHours(24));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetCatalogAsync(forceRefresh: true));
            Assert.Contains("fresh", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Installer_RejectsRevokedTrustedPublisherId_WhenManifestAuthorDiffers()
    {
        var root = Path.Combine(Path.GetTempPath(), "WinCarePublisherRevocation_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            using var package = CreatePackage("com.wincare.publisherrevoked", "Friendly Author");
            var installer = new PluginInstallerService(pluginsBaseDirectory: root);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                installer.InstallTrustedPluginFromStreamAsync(
                    package,
                    "com.wincare.publisherrevoked",
                    expectedSha256: null,
                    expectedPublisherId: "publisher-cert-123",
                    expectedPublisherPublicKeyPem: null,
                    expectedPublisherSignature: null,
                    revokedPackageIds: null,
                    revokedPublishers: new[] { "publisher-cert-123" },
                    consentedCapabilities: null));

            Assert.Contains("publisher-cert-123", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(root, "com.wincare.publisherrevoked")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Installer_StoresAdmissionOutsidePluginDirectory_AndManifestPlusLegacyDigestRewriteFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "WinCareAdmissionTrust_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            using var package = CreatePackage("com.wincare.externaltrust", "Publisher");
            var installer = new PluginInstallerService(pluginsBaseDirectory: root);
            var installedDir = await installer.InstallPluginFromStreamAsync(package, "com.wincare.externaltrust");

            var admissionPath = PluginAdmissionTrustStore.GetRecordPath(installedDir);
            Assert.True(File.Exists(admissionPath));
            Assert.False(admissionPath.StartsWith(
                Path.GetFullPath(installedDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
            Assert.False(File.Exists(Path.Combine(installedDir, PluginInstallerService.ManifestDigestFileName)));

            // The package helper intentionally emits an UTF-8 BOM. Installer and discovery
            // must accept it without changing the exact raw bytes covered by admission trust.
            var admitted = JsonPluginLoader.LoadFromDirectory(installedDir);
            Assert.True(admitted.Success, admitted.ErrorMessage);

            var manifestPath = Path.Combine(installedDir, "wincare-plugin.json");
            var tampered = File.ReadAllText(manifestPath).Replace("External Trust Plugin", "Tampered Plugin", StringComparison.Ordinal);
            File.WriteAllText(manifestPath, tampered);

            // Rewrite the old colocated checksum too. Discovery must still bind to the
            // external admission record created during installation.
            var forgedDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tampered))).ToLowerInvariant();
            File.WriteAllText(Path.Combine(installedDir, PluginInstallerService.ManifestDigestFileName), forgedDigest);

            var result = JsonPluginLoader.LoadFromDirectory(installedDir);
            Assert.False(result.Success);
            Assert.Contains("integrity", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AssemblyPlugin_EnableFailure_RollsBackDirectCommands_AndCleansRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "WinCareAssemblyRollback_" + Guid.NewGuid().ToString("N"));
        var pluginDir = Path.Combine(root, "com.wincare.rollbackassembly");
        Directory.CreateDirectory(pluginDir);
        var markerBase = Path.Combine(root, "cleanup-marker");

        try
        {
            string escapedMarker = markerBase.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
            string source = $$"""
            using System;
            using System.Collections.Generic;
            using System.IO;
            using System.Runtime.Versioning;
            using System.Threading;
            using System.Threading.Tasks;
            using WinCare.Application.Commands;
            using WinCare.Application.Plugins;
            using WinCare.CommandCatalog.Models;
            using WinCare.Domain.Commands;

            [assembly: TargetFramework(".NETCoreApp,Version=v8.0")]
            [assembly: SupportedOSPlatform("windows10.0.19041.0")]

            namespace Community.RollbackAssembly
            {
                public sealed class PluginEntryPoint : IWinCarePlugin
                {
                    public string Id => "com.wincare.rollbackassembly";
                    public string Name => "Rollback Assembly";
                    public string Version => "1.0.0";
                    public string Author => "Regression Test";
                    public string Description => "Registers an extra command before a later host rejection.";

                    public Task InitializeAsync(IPluginHost host, CancellationToken ct = default)
                    {
                        var extra = new CommandDefinition(
                            Id: "com.wincare.rollbackassembly.extra",
                            Title: "Extra",
                            Summary: "Extra command registered directly during InitializeAsync",
                            Area: "Utilities",
                            Section: "General",
                            Risk: CommandRisk.ReadOnly,
                            ReadOnly: true,
                            AdministratorAccess: AdministratorAccess.No,
                            Restart: RestartExpectation.No,
                            LegacySource: "plugin:test",
                            MigrationStatus: MigrationStatus.BehaviorVerified,
                            Keywords: Array.Empty<string>());

                        if (!host.RegisterCommand(extra, new ExtraHandler()))
                            throw new InvalidOperationException("Could not register extra command.");
                        return Task.CompletedTask;
                    }

                    public Task ShutdownAsync(CancellationToken ct = default)
                    {
                        File.WriteAllText("{{escapedMarker}}.shutdown", "1");
                        return Task.CompletedTask;
                    }

                    public ValueTask DisposeAsync()
                    {
                        File.WriteAllText("{{escapedMarker}}.dispose", "1");
                        return ValueTask.CompletedTask;
                    }

                    public IReadOnlyList<CommandDefinition> GetCommands() => Array.Empty<CommandDefinition>();
                    public IReadOnlyList<IPluginWidget> GetWidgets() => Array.Empty<IPluginWidget>();
                }

                public sealed class ExtraHandler : ICommandHandler
                {
                    public string CommandId => "com.wincare.rollbackassembly.extra";
                    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
                        => Task.FromResult(CommandHandlerOutcome.Succeeded("ok", "ok"));
                }
            }
            """;

            CompilePlugin(source, Path.Combine(pluginDir, "PluginAssembly.dll"));
            File.WriteAllText(Path.Combine(pluginDir, "wincare-plugin.json"), """
            {
              "id": "com.wincare.rollbackassembly",
              "name": "Rollback Assembly",
              "version": "1.0.0",
              "author": "Regression Test",
              "entryType": "Assembly",
              "targetFramework": "net8.0-windows10.0.19041.0",
              "assemblyFileName": "PluginAssembly.dll",
              "pluginClassName": "Community.RollbackAssembly.PluginEntryPoint",
              "tools": [
                {
                  "id": "com.wincare.rollbackassembly.declared",
                  "title": "Declared Without Handler",
                  "area": "Utilities",
                  "section": "General",
                  "risk": "ReadOnly",
                  "readOnly": true,
                  "executorType": "Assembly"
                }
              ]
            }
            """);

            var dispatcher = new CommandDispatcher(Array.Empty<CommandDefinition>(), Array.Empty<ICommandHandler>());
            var host = new DefaultPluginHost(dispatcher, pluginsUserDirectory: root);
            var registry = new PluginRegistryService(
                initialEnabledPluginIds: new HashSet<string> { "com.wincare.rollbackassembly" });

            await registry.DiscoverAndInitializeAsync(host);

            var plugin = Assert.Single(registry.GetAllPlugins(), item => item.Id == "com.wincare.rollbackassembly");
            Assert.Equal(PluginState.Error, plugin.State);
            Assert.DoesNotContain(host.RegisteredCommands, command => command.Id == "com.wincare.rollbackassembly.extra");
            Assert.DoesNotContain(host.RegisteredCommands, command => command.Id == "com.wincare.rollbackassembly.declared");
            Assert.True(File.Exists(markerBase + ".shutdown"));
            Assert.True(File.Exists(markerBase + ".dispose"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static RemotePluginItem CreateRemoteItem()
    {
        return new RemotePluginItem
        {
            Id = "com.wincare.sample",
            Name = "Sample",
            Version = "1.0.0",
            Author = "Publisher",
            PublisherId = "publisher-1",
            PackageUrl = "https://plugins.invalid/sample.zip",
            Sha256 = new string('a', 64),
            PublicKeyPem = "public-key",
            Signature = "signature",
            Permissions = new List<string> { "filesystem.read" }
        };
    }

    private static MemoryStream CreatePackage(string pluginId, string author)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = zip.CreateEntry("wincare-plugin.json");
            using var writer = new StreamWriter(manifest.Open(), Encoding.UTF8, bufferSize: 1024, leaveOpen: false);
            writer.Write(JsonSerializer.Serialize(new
            {
                id = pluginId,
                name = pluginId == "com.wincare.externaltrust" ? "External Trust Plugin" : "Publisher Revocation Plugin",
                version = "1.0.0",
                author,
                tools = Array.Empty<object>()
            }));
        }
        stream.Position = 0;
        return stream;
    }

    private static void CompilePlugin(string sourceCode, string assemblyFilePath)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
            MetadataReference.CreateFromFile(typeof(IWinCarePlugin).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(CommandDefinition).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(CommandRequest).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(JsonElement).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ICommandHandler).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create(
            "PluginAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var emitResult = compilation.Emit(assemblyFilePath);
        Assert.True(emitResult.Success, string.Join("\n", emitResult.Diagnostics.Select(diagnostic => diagnostic.GetMessage())));
    }

    private sealed class CatalogSignatureHttpHandler : HttpMessageHandler
    {
        private readonly string _catalogJson;
        private readonly string _signature;

        public CatalogSignatureHttpHandler(string catalogJson, string signature)
        {
            _catalogJson = catalogJson;
            _signature = signature;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bool isSignature = request.RequestUri?.AbsolutePath.EndsWith(".sig", StringComparison.OrdinalIgnoreCase) == true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(isSignature ? _signature : _catalogJson, Encoding.UTF8, isSignature ? "text/plain" : "application/json")
            });
        }
    }

    private sealed class StaticHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;

        public StaticHttpHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/json")
            });
        }
    }
}
