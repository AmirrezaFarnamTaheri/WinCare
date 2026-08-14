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
/// Infrastructure service for fetching online plugin catalog feed with 24h local disk caching.
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
    private readonly TimeSpan _cacheDuration;

    /// <summary>
    /// Initializes a new instance of <see cref="RemoteCatalogService"/>.
    /// </summary>
    public RemoteCatalogService(
        HttpClient? httpClient = null,
        string? cacheFilePath = null,
        string? catalogUrl = null,
        TimeSpan? cacheDuration = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WinCare/1.0 (Windows NT 10.0)");
        }
        _catalogUrl = catalogUrl ?? DefaultCatalogUrl;
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
    }

    /// <inheritdoc />
    public async Task<RemotePluginCatalog> GetCatalogAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && File.Exists(_cacheFilePath))
        {
            var fileInfo = new FileInfo(_cacheFilePath);
            if (DateTime.UtcNow - fileInfo.LastWriteTimeUtc < _cacheDuration)
            {
                var cachedCatalog = await TryLoadFromCacheAsync(cancellationToken).ConfigureAwait(false);
                if (cachedCatalog != null)
                {
                    return cachedCatalog;
                }
            }
        }

        try
        {
            using var response = await _httpClient.GetAsync(_catalogUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var catalog = await JsonSerializer.DeserializeAsync<RemotePluginCatalog>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? new RemotePluginCatalog();

            catalog.LastUpdated = DateTime.UtcNow;
            await SaveToCacheAsync(catalog, cancellationToken).ConfigureAwait(false);

            return catalog;
        }
        catch (Exception ex)
        {
            var fallbackCatalog = await TryLoadFromCacheAsync(cancellationToken).ConfigureAwait(false);
            if (fallbackCatalog != null)
            {
                return fallbackCatalog;
            }

            throw new InvalidOperationException(
                $"Failed to fetch remote catalog from '{_catalogUrl}' and no valid local cache exists at '{_cacheFilePath}'.",
                ex);
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
                p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Author.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.CommandsProvided.Any(c => c.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        return plugins.ToList().AsReadOnly();
    }

    private async Task<RemotePluginCatalog?> TryLoadFromCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_cacheFilePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(_cacheFilePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<RemotePluginCatalog>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveToCacheAsync(RemotePluginCatalog catalog, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(catalog, JsonOptions);
            var tempFilePath = _cacheFilePath + ".tmp." + Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(tempFilePath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempFilePath, _cacheFilePath, overwrite: true);
        }
        catch
        {
            // Best effort cache write
        }
    }
}
