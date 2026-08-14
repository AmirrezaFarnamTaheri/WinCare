using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinCare.Infrastructure.Plugins;

public interface IRemoteCatalogService
{
    Task<RemotePluginCatalog> GetCatalogAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemotePluginItem>> SearchPluginsAsync(string query, string? category = null, CancellationToken cancellationToken = default);
}

public class RemoteCatalogService : IRemoteCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public const string DefaultCatalogUrl = "https://raw.githubusercontent.com/WinCare/plugin-catalog/main/catalog.json";
    
    private readonly HttpClient _httpClient;
    private readonly string _cacheFilePath;
    private readonly string _catalogUrl;
    private readonly TimeSpan _cacheDuration;

    public RemoteCatalogService(
        HttpClient? httpClient = null,
        string? cacheFilePath = null,
        string? catalogUrl = null,
        TimeSpan? cacheDuration = null)
    {
        _httpClient = httpClient ?? new HttpClient();
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
            var response = await _httpClient.GetAsync(_catalogUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var catalog = JsonSerializer.Deserialize<RemotePluginCatalog>(json, JsonOptions)
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
            await File.WriteAllTextAsync(_cacheFilePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best effort cache write
        }
    }
}
