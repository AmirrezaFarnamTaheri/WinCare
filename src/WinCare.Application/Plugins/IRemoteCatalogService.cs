namespace WinCare.Application.Plugins;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IRemoteCatalogService
{
    Task<RemotePluginCatalog> GetCatalogAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RemotePluginItem>> SearchPluginsAsync(string query, string? category = null, CancellationToken cancellationToken = default);
}
