namespace WinCare.App.ViewModels.Pages;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using WinCare.App.Services;
using WinCare.Application.Plugins;

/// <summary>
/// ViewModel managing the Plugin Store catalog, search filtering, and package installation.
/// </summary>
public sealed class PluginStorePageViewModel : INotifyPropertyChanged
{
    private readonly IPluginRegistry _registry;
    private readonly IRemoteCatalogService _catalogService;
    private readonly IPluginInstallerService _installerService;
    private readonly IPluginHost _host;

    private string _searchQuery = string.Empty;
    private string _selectedCategory = "All";
    private bool _isLoading;
    private CancellationTokenSource? _searchCts;

    /// <summary>
    /// Event fired when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Initializes a new instance of <see cref="PluginStorePageViewModel"/> using supplied or shared runtime services.
    /// </summary>
    public PluginStorePageViewModel(
        IPluginRegistry? registry = null,
        IRemoteCatalogService? catalogService = null,
        IPluginInstallerService? installerService = null,
        IPluginHost? host = null)
    {
        _host = host ?? AppRuntime.Current.PluginHost;
        _registry = registry ?? AppRuntime.Current.PluginRegistry;
        _catalogService = catalogService ?? AppRuntime.Current.CatalogService;
        _installerService = installerService ?? AppRuntime.Current.InstallerService;

        Plugins = new ObservableCollection<PluginCardViewModel>();
        Categories = new ObservableCollection<string> { "All", "System Care", "Security", "Utilities", "Installed" };
    }

    /// <summary>
    /// Collection of visual plugin cards displayed in the store list.
    /// </summary>
    public ObservableCollection<PluginCardViewModel> Plugins { get; }

    /// <summary>
    /// Collection of category tab filter names.
    /// </summary>
    public ObservableCollection<string> Categories { get; }

    /// <summary>
    /// Search filter query string with debounced auto-filtering.
    /// </summary>
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery != value)
            {
                _searchQuery = value;
                OnPropertyChanged();
                _searchCts?.Cancel();
                _searchCts = new CancellationTokenSource();
                _ = TriggerDebouncedSearchAsync(_searchCts.Token);
            }
        }
    }

    /// <summary>
    /// Currently selected category filter.
    /// </summary>
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (_selectedCategory != value)
            {
                _selectedCategory = value;
                OnPropertyChanged();
                _ = RefreshPluginsAsync();
            }
        }
    }

    /// <summary>
    /// Whether the store catalog is currently fetching data or refreshing.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading != value)
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Initializes discovery and loads installed/online plugin catalog.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _registry.DiscoverAndInitializeAsync(_host, cancellationToken).ConfigureAwait(true);
        }
        catch
        {
            // Tolerate initial discovery faults to show offline state
        }

        await RefreshPluginsAsync(forceRemoteRefresh: false, cancellationToken).ConfigureAwait(true);
    }

    private async Task TriggerDebouncedSearchAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            if (!token.IsCancellationRequested)
            {
                await RefreshPluginsAsync(forceRemoteRefresh: false, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Keystroke debounced
        }
    }

    /// <summary>
    /// Refreshes the active plugin catalog list from registry and remote store.
    /// </summary>
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

    /// <summary>
    /// Installs a remote plugin package, runs discovery, and refreshes the store.
    /// </summary>
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
