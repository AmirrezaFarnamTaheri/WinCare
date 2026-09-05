using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Plugins;

namespace WinCare.Infrastructure.Plugins;

/// <summary>
/// Infrastructure service for fetching online plugin catalog feed with 24h local disk caching and offline fallbacks.
/// </summary>
public class RemoteCatalogService : IRemoteCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>
    /// Default GitHub Raw CDN endpoint for official plugin catalog feed.
    /// </summary>
    public const string DefaultCatalogUrl = "https://raw.githubusercontent.com/WinCare/plugin-catalog/main/catalog.json";
    
    private readonly HttpClient _httpClient;
    private readonly string _cacheFilePath;
    private readonly string _catalogUrl;
    private readonly string _catalogSignatureUrl;
    private readonly string? _trustedCatalogPublicKeyPem;
    private readonly string _cacheSignatureFilePath;
    private readonly TimeSpan _cacheDuration;

    /// <summary>
    /// Initializes a new instance of <see cref="RemoteCatalogService"/>.
    /// </summary>
    public RemoteCatalogService(
        HttpClient? httpClient = null,
        string? cacheFilePath = null,
        string? catalogUrl = null,
        TimeSpan? cacheDuration = null,
        string? trustedCatalogPublicKeyPem = null,
        string? catalogSignatureUrl = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WinCare/1.0 (Windows NT 10.0)");
        }
        _catalogUrl = catalogUrl ?? DefaultCatalogUrl;
        _catalogSignatureUrl = catalogSignatureUrl ?? (_catalogUrl + ".sig");
        _trustedCatalogPublicKeyPem = string.IsNullOrWhiteSpace(trustedCatalogPublicKeyPem) ? null : trustedCatalogPublicKeyPem;
        _cacheDuration = cacheDuration ?? TimeSpan.FromHours(24);

        if (string.IsNullOrWhiteSpace(cacheFilePath))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var winCareDir = Path.Combine(localAppData, "WinCare");
            Directory.CreateDirectory(winCareDir);
            _cacheFilePath = Path.Combine(winCareDir, "catalog_cache.json");
        }
        else
        {
            _cacheFilePath = cacheFilePath;
            var parentDir = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }
        }

        _cacheSignatureFilePath = _cacheFilePath + ".sig";
    }

    /// <inheritdoc />
    public async Task<RemotePluginCatalog> GetCatalogAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!forceRefresh && File.Exists(_cacheFilePath))
        {
            var fileInfo = new FileInfo(_cacheFilePath);
            if (DateTime.UtcNow - fileInfo.LastWriteTimeUtc < _cacheDuration)
            {
                var cachedCatalog = await TryLoadFromCacheAsync(cancellationToken).ConfigureAwait(false);
                if (cachedCatalog != null)
                {
                    ApplyRevocationPolicy(cachedCatalog);
                    return cachedCatalog;
                }
            }
        }

        try
        {
            using var response = await _httpClient.GetAsync(_catalogUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            byte[] catalogBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            string? detachedSignature = null;
            bool trustVerified = false;
            string trustMessage;
            if (_trustedCatalogPublicKeyPem is not null)
            {
                using var signatureResponse = await _httpClient.GetAsync(_catalogSignatureUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                signatureResponse.EnsureSuccessStatusCode();
                detachedSignature = (await signatureResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
                trustVerified = PluginAdmissionTrustStore.VerifyManifestSignature(
                    catalogBytes,
                    detachedSignature,
                    _trustedCatalogPublicKeyPem);
                if (!trustVerified)
                {
                    throw new InvalidOperationException(
                        "The plugin catalog detached signature did not verify against the WinCare-pinned catalog trust root.");
                }
                trustMessage = "Catalog signature verified against the WinCare-pinned trust root.";
            }
            else
            {
                trustMessage = "Catalog browsing is available, but remote installation is disabled because this build has no pinned catalog signing key.";
            }

            var catalog = JsonSerializer.Deserialize<RemotePluginCatalog>(catalogBytes, JsonOptions)
                ?? new RemotePluginCatalog();
            NormalizeCatalog(catalog);
            SetTrustState(catalog, trustVerified, trustMessage);
            ApplyRevocationPolicy(catalog);
            await SaveToCacheAsync(catalogBytes, detachedSignature, cancellationToken).ConfigureAwait(false);

            return catalog;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RemoteCatalogService] Fetch failed from '{_catalogUrl}': {ex.Message}");

            // A forced refresh is used by the install path as a security boundary. Falling
            // back to an arbitrarily old cache here would silently weaken revocation and
            // publisher-key freshness, so security-sensitive callers fail closed instead.
            if (forceRefresh)
            {
                throw new InvalidOperationException(
                    "A fresh plugin security catalog could not be obtained. Installation is blocked until the current catalog and revocation advisory can be verified.",
                    ex);
            }

            var fallbackCatalog = await TryLoadFromCacheAsync(cancellationToken).ConfigureAwait(false);
            if (fallbackCatalog != null)
            {
                ApplyRevocationPolicy(fallbackCatalog);
                return fallbackCatalog;
            }

            // Return bundled offline default catalog with illustrative (non-installable) sample metadata
            var defaultCatalog = GetOfflineDefaultCatalog();
            ApplyRevocationPolicy(defaultCatalog);
            return defaultCatalog;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RemotePluginItem>> SearchPluginsAsync(string query, string? category = null, CancellationToken cancellationToken = default)
    {
        var catalog = await GetCatalogAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
        var plugins = catalog.Plugins.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
        {
            plugins = plugins.Where(p => string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            plugins = plugins.Where(p =>
                p.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Author.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.CommandsProvided.Any(c => c.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        return plugins.ToList().AsReadOnly();
    }

    private static void ApplyRevocationPolicy(RemotePluginCatalog catalog)
    {
        if (catalog?.Plugins == null) return;
        var revokedPkgs = new HashSet<string>(catalog.RevokedPackages ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var revokedPubs = new HashSet<string>(catalog.RevokedPublishers ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in catalog.Plugins)
        {
            if (revokedPkgs.Contains(plugin.Id))
            {
                plugin.IsRevoked = true;
                plugin.RevocationReason ??= "Package ID is listed on the security revocation advisory.";
            }
            else if (!string.IsNullOrEmpty(plugin.PublisherId) && revokedPubs.Contains(plugin.PublisherId))
            {
                plugin.IsRevoked = true;
                plugin.RevocationReason ??= "Publisher certificate is listed on the security revocation advisory.";
            }
            else if (!string.IsNullOrEmpty(plugin.Author) && revokedPubs.Contains(plugin.Author))
            {
                plugin.IsRevoked = true;
                plugin.RevocationReason ??= "Author entity is listed on the security revocation advisory.";
            }
        }
    }

    private static void NormalizeCatalog(RemotePluginCatalog catalog)
    {
        catalog.Plugins ??= new List<RemotePluginItem>();
        catalog.RevokedPackages ??= new List<string>();
        catalog.RevokedPublishers ??= new List<string>();

        foreach (RemotePluginItem plugin in catalog.Plugins)
        {
            plugin.Id ??= string.Empty;
            plugin.Name ??= string.Empty;
            plugin.Description ??= string.Empty;
            plugin.Author ??= string.Empty;
            plugin.Category ??= string.Empty;
            plugin.CommandsProvided ??= new List<string>();
            plugin.Permissions ??= new List<string>();
        }
    }

    private static RemotePluginCatalog GetOfflineDefaultCatalog()
    {
        // Offline fallback entries are illustrative sample metadata only. They are not
        // installable: PackageUrl and Sha256 are intentionally empty so the offline catalog
        // never fabricates a download endpoint or an integrity digest for packages that are
        // not actually distributable through it.
        var catalog = new RemotePluginCatalog
        {
            CatalogVersion = "1.0",
            LastUpdated = DateTime.UtcNow,
            Plugins = new List<RemotePluginItem>
            {
                new()
                {
                    Id = "org.wincare.diskcleaner",
                    Name = "Enhanced Disk Cleaner",
                    Author = "WinCare Community",
                    Version = "1.0.0",
                    Description = "Deep cleaner for Windows temp files, browser caches, and delivery optimization files. (Offline sample — not installable.)",
                    Category = "System Care",
                    Sha256 = string.Empty,
                    PackageUrl = string.Empty,
                    Permissions = new List<string> { "filesystem.read", "filesystem.write" },
                    CommandsProvided = new List<string> { "org.wincare.diskcleaner.run" }
                },
                new()
                {
                    Id = "org.wincare.dnstools",
                    Name = "Network DNS Diagnostic Kit",
                    Author = "WinCare Network Group",
                    Version = "1.1.0",
                    Description = "Flush DNS cache, test DNS latency across multiple providers, and reset Winsock. (Offline sample — not installable.)",
                    Category = "Utilities",
                    Sha256 = string.Empty,
                    PackageUrl = string.Empty,
                    Permissions = new List<string> { "network.query", "process.spawn" },
                    CommandsProvided = new List<string> { "org.wincare.dnstools.flush", "org.wincare.dnstools.bench" }
                }
            }
        };
        SetTrustState(catalog, false, "Offline sample catalog. Remote installation is unavailable.");
        return catalog;
    }

    private async Task<RemotePluginCatalog?> TryLoadFromCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_cacheFilePath))
            {
                return null;
            }

            byte[] catalogBytes = await File.ReadAllBytesAsync(_cacheFilePath, cancellationToken).ConfigureAwait(false);
            bool trustVerified = false;
            string trustMessage;
            if (_trustedCatalogPublicKeyPem is not null)
            {
                if (!File.Exists(_cacheSignatureFilePath))
                {
                    return null;
                }
                string detachedSignature = (await File.ReadAllTextAsync(_cacheSignatureFilePath, cancellationToken).ConfigureAwait(false)).Trim();
                trustVerified = PluginAdmissionTrustStore.VerifyManifestSignature(
                    catalogBytes,
                    detachedSignature,
                    _trustedCatalogPublicKeyPem);
                if (!trustVerified)
                {
                    return null;
                }
                trustMessage = "Cached catalog signature verified against the WinCare-pinned trust root.";
            }
            else
            {
                trustMessage = "Cached catalog is available for browsing, but remote installation is disabled because this build has no pinned catalog signing key.";
            }

            RemotePluginCatalog? catalog = JsonSerializer.Deserialize<RemotePluginCatalog>(catalogBytes, JsonOptions);
            if (catalog != null)
            {
                NormalizeCatalog(catalog);
                SetTrustState(catalog, trustVerified, trustMessage);
            }
            return catalog;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveToCacheAsync(byte[] catalogBytes, string? detachedSignature, CancellationToken cancellationToken)
    {
        try
        {
            var tempFilePath = _cacheFilePath + ".tmp." + Guid.NewGuid().ToString("N");
            await File.WriteAllBytesAsync(tempFilePath, catalogBytes, cancellationToken).ConfigureAwait(false);
            File.Move(tempFilePath, _cacheFilePath, overwrite: true);

            if (!string.IsNullOrWhiteSpace(detachedSignature))
            {
                var signatureTemp = _cacheSignatureFilePath + ".tmp." + Guid.NewGuid().ToString("N");
                await File.WriteAllTextAsync(signatureTemp, detachedSignature, cancellationToken).ConfigureAwait(false);
                File.Move(signatureTemp, _cacheSignatureFilePath, overwrite: true);
            }
            else if (File.Exists(_cacheSignatureFilePath))
            {
                File.Delete(_cacheSignatureFilePath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RemoteCatalogService] Cache save error: {ex.Message}");
        }
    }

    private static void SetTrustState(RemotePluginCatalog catalog, bool verified, string message)
    {
        catalog.IsTrustVerified = verified;
        catalog.TrustStatusMessage = message;
        foreach (RemotePluginItem item in catalog.Plugins ?? new List<RemotePluginItem>())
        {
            item.IsCatalogTrustVerified = verified;
        }
    }
}
