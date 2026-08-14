namespace WinCare.App.ViewModels.Pages;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Plugins;
using WinCare.Infrastructure.Plugins;

public sealed class PluginStorePageViewModel
{
    private readonly IPluginRegistry _registry;
    private readonly IRemoteCatalogService _catalogService;
    private readonly IPluginInstallerService _installerService;
    private readonly IPluginHost _host;

    private string _searchQuery = string.Empty;
    private string _selectedCategory = "All";
    private bool _isLoading;

    public PluginStorePageViewModel(
        IPluginRegistry? registry = null,
        IRemoteCatalogService? catalogService = null,
        IPluginInstallerService? installerService = null,
        IPluginHost? host = null)
    {
        _host = host ?? new DefaultPluginHost();
        _registry = registry ?? new PluginRegistryService();
        _catalogService = catalogService ?? new RemoteCatalogService();
        _installerService = installerService ?? new PluginInstallerService();

        Plugins = new ObservableCollection<PluginCardViewModel>();
        Categories = new ObservableCollection<string> { "All", "System Care", "Security", "Utilities", "Installed" };
    }

    public ObservableCollection<PluginCardViewModel> Plugins { get; }
    public ObservableCollection<string> Categories { get; }

    private CancellationTokenSource? _searchCts;

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery != value)
            {
                _searchQuery = value;
                _searchCts?.Cancel();
                _searchCts = new CancellationTokenSource();
                var token = _searchCts.Token;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(300, token).ConfigureAwait(true);
                        await RefreshPluginsAsync(forceRemoteRefresh: false, token).ConfigureAwait(true);
                    }
                    catch (OperationCanceledException)
                    {
                        // Keystroke debounced
                    }
                }, token);
            }
        }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (_selectedCategory != value)
            {
                _selectedCategory = value;
                _ = RefreshPluginsAsync();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => _isLoading = value;
    }

    public async Task RefreshPluginsAsync(bool forceRemoteRefresh = false, CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            Plugins.Clear();

            var installedPlugins = _registry.GetAllPlugins().ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

            if (string.Equals(_selectedCategory, "Installed", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var installed in installedPlugins.Values)
                {
                    if (MatchesSearch(installed.Name, installed.Description, installed.Author, installed.Category))
                    {
                        Plugins.Add(new PluginCardViewModel(installed));
                    }
                }
                return;
            }

            // Fetch online catalog
            RemotePluginCatalog catalog;
            try
            {
                catalog = await _catalogService.GetCatalogAsync(forceRemoteRefresh, cancellationToken).ConfigureAwait(true);
            }
            catch
            {
                catalog = new RemotePluginCatalog();
            }

            // Combine installed and remote catalog items
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in catalog.Plugins)
            {
                seenIds.Add(item.Id);
                var isInstalled = installedPlugins.TryGetValue(item.Id, out var installedEntry);

                if (MatchesCategory(item.Category) && MatchesSearch(item.Name, item.Description, item.Author, item.Category))
                {
                    Plugins.Add(new PluginCardViewModel(item, isInstalled, installedEntry));
                }
            }

            // Add installed plugins not present in online catalog
            foreach (var installed in installedPlugins.Values)
            {
                if (!seenIds.Contains(installed.Id))
                {
                    if (MatchesCategory(installed.Category) && MatchesSearch(installed.Name, installed.Description, installed.Author, installed.Category))
                    {
                        Plugins.Add(new PluginCardViewModel(installed));
                    }
                }
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task<bool> InstallPluginAsync(PluginCardViewModel card, CancellationToken cancellationToken = default)
    {
        if (card == null || card.IsInstalled || string.IsNullOrWhiteSpace(card.PackageUrl))
        {
            return false;
        }

        try
        {
            await _installerService.InstallPluginFromPackageAsync(card.PackageUrl, cancellationToken).ConfigureAwait(true);
            await _registry.DiscoverAndInitializeAsync(_host, cancellationToken).ConfigureAwait(true);
            await RefreshPluginsAsync(forceRemoteRefresh: false, cancellationToken).ConfigureAwait(true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool MatchesCategory(string category)
    {
        if (string.Equals(_selectedCategory, "All", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return string.Equals(category, _selectedCategory, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesSearch(string name, string description, string author, string category)
    {
        if (string.IsNullOrWhiteSpace(_searchQuery))
        {
            return true;
        }

        var q = _searchQuery.Trim();
        return name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               author.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               category.Contains(q, StringComparison.OrdinalIgnoreCase);
    }
}
