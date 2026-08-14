namespace WinCare.Application.Plugins;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Service contract for fetching and searching the remote WinCare plugin catalog.
/// </summary>
public interface IRemoteCatalogService
{
    /// <summary>
    /// Fetches the remote plugin catalog with automatic 24-hour local caching.
    /// </summary>
    Task<RemotePluginCatalog> GetCatalogAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches the plugin catalog by query text and optional category filter.
    /// </summary>
    Task<IReadOnlyList<RemotePluginItem>> SearchPluginsAsync(string query, string? category = null, CancellationToken cancellationToken = default);
}
