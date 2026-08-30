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
public sealed class PluginStorePageViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IPluginRegistry _registry;
    private readonly IRemoteCatalogService _catalogService;
    private readonly IPluginInstallerService _installerService;
    private readonly IPluginHost _host;
    private readonly bool _usesSharedRuntime;

    private string _searchQuery = string.Empty;
    private string _selectedCategory = "All";
    private bool _isLoading;
    private string? _errorMessage;
    private CancellationTokenSource? _searchCts;
    private long _refreshVersion;

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
        _usesSharedRuntime = registry is null && host is null;
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
                var previous = _searchCts;
                _searchCts = new CancellationTokenSource();
                previous?.Cancel();
                previous?.Dispose();
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
                _ = RefreshAfterFilterChangeAsync();
            }
        }
    }

    /// <summary>
    /// Last error message from plugin operations, if any.
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (_errorMessage != value)
            {
                _errorMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    /// <summary>
    /// Whether an error banner should be displayed.
    /// </summary>
    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);

    public bool IsEmpty => !IsLoading && Plugins.Count == 0;

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
                OnPropertyChanged(nameof(IsEmpty));
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
            if (_usesSharedRuntime)
            {
                await AppRuntime.Current.InitializePluginsAsync(cancellationToken).ConfigureAwait(true);
            }
            else
            {
                await _registry.DiscoverAndInitializeAsync(_host, cancellationToken).ConfigureAwait(true);
            }
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
        catch (Exception ex)
        {
            ErrorMessage = $"Search refresh failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[PluginStorePageViewModel] Search refresh error: {ex}");
        }
    }

    private async Task RefreshAfterFilterChangeAsync()
    {
        try
        {
            await RefreshPluginsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Catalog refresh failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[PluginStorePageViewModel] Filter refresh error: {ex}");
        }
    }

    /// <summary>
    /// Refreshes the active plugin catalog list from registry and remote store.
    /// </summary>
    public async Task RefreshPluginsAsync(bool forceRemoteRefresh = false, CancellationToken cancellationToken = default)
    {
        var refreshVersion = Interlocked.Increment(ref _refreshVersion);
        var selectedCategory = _selectedCategory;
        var searchQuery = _searchQuery;
        IsLoading = true;
        try
        {
            var installedPlugins = _registry.GetAllPlugins().ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
            var cards = new List<PluginCardViewModel>();

            if (string.Equals(selectedCategory, "Installed", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var installed in installedPlugins.Values)
                {
                    if (MatchesSearch(searchQuery, installed.Id, installed.Name, installed.Description, installed.Author, installed.Category, installed.Commands.Select(command => command.Id)))
                    {
                        cards.Add(new PluginCardViewModel(installed));
                    }
                }
            }
            else
            {
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

                    if (MatchesCategory(item.Category, selectedCategory) && MatchesSearch(searchQuery, item.Id, item.Name, item.Description, item.Author, item.Category, item.CommandsProvided))
                    {
                        cards.Add(new PluginCardViewModel(item, isInstalled, installedEntry));
                    }
                }

                // Add installed plugins not present in online catalog
                foreach (var installed in installedPlugins.Values)
                {
                    if (!seenIds.Contains(installed.Id) &&
                        MatchesCategory(installed.Category, selectedCategory) &&
                        MatchesSearch(searchQuery, installed.Id, installed.Name, installed.Description, installed.Author, installed.Category, installed.Commands.Select(command => command.Id)))
                    {
                        cards.Add(new PluginCardViewModel(installed));
                    }
                }
            }

            // A more recent search/category request owns the collection. Older async
            // requests may finish later, but must never overwrite its visible results.
            if (refreshVersion != Volatile.Read(ref _refreshVersion))
            {
                return;
            }

            Plugins.Clear();
            foreach (var card in cards)
            {
                Plugins.Add(card);
            }
            OnPropertyChanged(nameof(IsEmpty));
        }
        finally
        {
            if (refreshVersion == Volatile.Read(ref _refreshVersion))
            {
                IsLoading = false;
            }
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

        ErrorMessage = null;
        try
        {
            // The remote catalog is transport metadata, not an independent publisher trust
            // root. Never let it choose the key used to establish package authenticity.
            ErrorMessage = "Remote plugin installation is disabled for this release.";
            return false;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Install failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[PluginStorePageViewModel] Install error: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Enables an installed plugin.
    /// </summary>
    public async Task<bool> EnablePluginAsync(PluginCardViewModel card, CancellationToken cancellationToken = default)
    {
        if (card == null || !card.IsInstalled || card.IsBuiltIn)
        {
            return false;
        }

        ErrorMessage = null;
        try
        {
            await _registry.EnablePluginAsync(card.Id, _host, cancellationToken).ConfigureAwait(true);
            await RefreshPluginsAsync(forceRemoteRefresh: false, cancellationToken).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Enable failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[PluginStorePageViewModel] Enable error: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Disables an installed plugin.
    /// </summary>
    public async Task<bool> DisablePluginAsync(PluginCardViewModel card, CancellationToken cancellationToken = default)
    {
        if (card == null || !card.IsInstalled || card.IsBuiltIn)
        {
            return false;
        }

        ErrorMessage = null;
        try
        {
            await _registry.DisablePluginAsync(card.Id, _host, cancellationToken).ConfigureAwait(true);
            await RefreshPluginsAsync(forceRemoteRefresh: false, cancellationToken).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Disable failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[PluginStorePageViewModel] Disable error: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Uninstalls an installed plugin.
    /// </summary>
    public async Task<bool> UninstallPluginAsync(PluginCardViewModel card, CancellationToken cancellationToken = default)
    {
        if (card == null || !card.IsInstalled || card.IsBuiltIn)
        {
            return false;
        }

        ErrorMessage = null;
        try
        {
            await _registry.DisablePluginAsync(card.Id, _host, cancellationToken).ConfigureAwait(true);
            await _installerService.UninstallPluginAsync(card.Id, cancellationToken).ConfigureAwait(true);
            await _registry.DiscoverAndInitializeAsync(_host, cancellationToken).ConfigureAwait(true);
            await RefreshPluginsAsync(forceRemoteRefresh: false, cancellationToken).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Uninstall failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[PluginStorePageViewModel] Uninstall error: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    private static bool MatchesCategory(string category, string selectedCategory)
    {
        if (string.Equals(selectedCategory, "All", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return string.Equals(category, selectedCategory, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesSearch(string searchQuery, string id, string name, string description, string author, string category, IEnumerable<string> commands)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return true;
        }

        var q = searchQuery.Trim();
        return id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               author.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               commands.Any(command => command.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
    }
}
