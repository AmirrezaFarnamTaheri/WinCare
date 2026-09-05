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
/// ViewModel managing the Plugin Store catalog, search filtering, trust status, and package installation.
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
    private string _catalogStatusMessage = "Catalog trust has not been evaluated yet.";
    private bool _isCatalogTrustVerified;
    private CancellationTokenSource? _searchCts;
    private long _refreshVersion;

    public event PropertyChangedEventHandler? PropertyChanged;

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

    public ObservableCollection<PluginCardViewModel> Plugins { get; }
    public ObservableCollection<string> Categories { get; }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery == value) return;
            _searchQuery = value;
            OnPropertyChanged();
            var previous = _searchCts;
            _searchCts = new CancellationTokenSource();
            previous?.Cancel();
            previous?.Dispose();
            _ = TriggerDebouncedSearchAsync(_searchCts.Token);
        }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (_selectedCategory == value) return;
            _selectedCategory = value;
            OnPropertyChanged();
            _ = RefreshAfterFilterChangeAsync();
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (_errorMessage == value) return;
            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);

    /// <summary>Runtime trust/freshness description for the currently displayed catalog.</summary>
    public string CatalogStatusMessage
    {
        get => _catalogStatusMessage;
        private set
        {
            if (_catalogStatusMessage == value) return;
            _catalogStatusMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCatalogStatus));
        }
    }

    public bool HasCatalogStatus => !string.IsNullOrWhiteSpace(CatalogStatusMessage);

    /// <summary>
    /// True only when the exact remote catalog bytes verified against a configured WinCare-pinned
    /// catalog key. The current repository build intentionally remains browse-only when no approved
    /// production catalog root is shipped.
    /// </summary>
    public bool IsCatalogTrustVerified
    {
        get => _isCatalogTrustVerified;
        private set
        {
            if (_isCatalogTrustVerified == value) return;
            _isCatalogTrustVerified = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCatalogBrowseOnly));
        }
    }

    public bool IsCatalogBrowseOnly => !IsCatalogTrustVerified;
    public bool IsEmpty => !IsLoading && Plugins.Count == 0;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value) return;
            _isLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

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
        catch (Exception ex)
        {
            ErrorMessage = "Plugin discovery did not complete. Installed plugin state may be incomplete until discovery succeeds.";
            System.Diagnostics.Debug.WriteLine($"[PluginStorePageViewModel] Initial discovery error: {ex}");
        }

        await RefreshPluginsAsync(forceRemoteRefresh: false, cancellationToken).ConfigureAwait(true);
    }

    private async Task TriggerDebouncedSearchAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            if (!token.IsCancellationRequested)
                await RefreshPluginsAsync(forceRemoteRefresh: false, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Keystroke debounce.
        }
        catch (Exception ex)
        {
            ErrorMessage = "Plugin search could not be refreshed. Installed plugins remain available.";
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
            ErrorMessage = "The online plugin catalog could not be refreshed. Installed plugins remain available.";
            System.Diagnostics.Debug.WriteLine($"[PluginStorePageViewModel] Filter refresh error: {ex}");
        }
    }

    public async Task RefreshPluginsAsync(bool forceRemoteRefresh = false, CancellationToken cancellationToken = default)
    {
        long refreshVersion = Interlocked.Increment(ref _refreshVersion);
        string selectedCategory = _selectedCategory;
        string searchQuery = _searchQuery;
        IsLoading = true;
        try
        {
            var installedPlugins = _registry.GetAllPlugins().ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
            var cards = new List<PluginCardViewModel>();

            if (string.Equals(selectedCategory, "Installed", StringComparison.OrdinalIgnoreCase))
            {
                CatalogStatusMessage = "Showing locally installed plugins. Remote catalog trust does not affect this view.";
                IsCatalogTrustVerified = false;
                foreach (var installed in installedPlugins.Values)
                {
                    if (MatchesSearch(searchQuery, installed.Id, installed.Name, installed.Description, installed.Author, installed.Category, installed.Commands.Select(command => command.Id)))
                        cards.Add(new PluginCardViewModel(installed));
                }
            }
            else
            {
                RemotePluginCatalog catalog;
                try
                {
                    catalog = await _catalogService.GetCatalogAsync(forceRemoteRefresh, cancellationToken).ConfigureAwait(true);
                    CatalogStatusMessage = catalog.TrustStatusMessage;
                    IsCatalogTrustVerified = catalog.IsTrustVerified;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    catalog = new RemotePluginCatalog
                    {
                        TrustStatusMessage = "The online plugin catalog is currently unavailable. Installed plugins are still shown. Remote installation is unavailable."
                    };
                    CatalogStatusMessage = catalog.TrustStatusMessage;
                    IsCatalogTrustVerified = false;
                    ErrorMessage = "The online plugin catalog could not be loaded. Installed plugins remain available.";
                    System.Diagnostics.Debug.WriteLine($"[PluginStorePageViewModel] Catalog refresh error: {ex}");
                }

                var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in catalog.Plugins)
                {
                    seenIds.Add(item.Id);
                    bool isInstalled = installedPlugins.TryGetValue(item.Id, out var installedEntry);
                    if (MatchesCategory(item.Category, selectedCategory) && MatchesSearch(searchQuery, item.Id, item.Name, item.Description, item.Author, item.Category, item.CommandsProvided))
                        cards.Add(new PluginCardViewModel(item, isInstalled, installedEntry));
                }

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

            if (refreshVersion != Volatile.Read(ref _refreshVersion)) return;

            Plugins.Clear();
            foreach (var card in cards) Plugins.Add(card);
            OnPropertyChanged(nameof(IsEmpty));
        }
        finally
        {
            if (refreshVersion == Volatile.Read(ref _refreshVersion)) IsLoading = false;
        }
    }

    public async Task<bool> InstallPluginAsync(PluginCardViewModel card, IReadOnlyCollection<string>? consentedCapabilities = null, CancellationToken cancellationToken = default)
    {
        if (card == null || card.IsInstalled || !card.CanInstall || card.RemoteItem is null) return false;

        ErrorMessage = null;
        try
        {
            var catalog = await _catalogService.GetCatalogAsync(forceRefresh: true, cancellationToken).ConfigureAwait(true);
            CatalogStatusMessage = catalog.TrustStatusMessage;
            IsCatalogTrustVerified = catalog.IsTrustVerified;
            if (!catalog.IsTrustVerified)
                throw new InvalidOperationException("Remote installation requires a catalog verified against a WinCare-pinned trust root.");

            var consented = consentedCapabilities ?? Array.Empty<string>();
            RemotePluginItem freshItem = RemotePluginInstallPolicy.ResolveFreshReviewedEntry(catalog, card.RemoteItem, consented);

            string trustedPublisherId = !string.IsNullOrWhiteSpace(freshItem.PublisherId)
                ? freshItem.PublisherId
                : freshItem.Author;
            if (string.IsNullOrWhiteSpace(trustedPublisherId))
                throw new InvalidOperationException("The current catalog entry has no stable publisher identity.");

            await _installerService.InstallTrustedPluginFromPackageAsync(
                freshItem.PackageUrl,
                freshItem.Id,
                freshItem.Sha256,
                trustedPublisherId,
                freshItem.PublicKeyPem!,
                freshItem.Signature!,
                catalog.RevokedPackages,
                catalog.RevokedPublishers,
                consented,
                cancellationToken).ConfigureAwait(true);

            await _registry.DiscoverAndInitializeAsync(_host, cancellationToken).ConfigureAwait(true);
            await RefreshPluginsAsync(forceRemoteRefresh: false, cancellationToken).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex is OperationCanceledException
                ? "Plugin installation was cancelled."
                : "Plugin installation was blocked or failed. Review the catalog trust status and package details before retrying.";
            System.Diagnostics.Debug.WriteLine($"[PluginStorePageViewModel] Install error: {ex}");
            return false;
        }
    }

    public async Task<bool> EnablePluginAsync(PluginCardViewModel card, CancellationToken cancellationToken = default)
    {
        if (card == null || !card.IsInstalled || card.IsBuiltIn) return false;

        ErrorMessage = null;
        try
        {
            await _registry.EnablePluginAsync(card.Id, _host, cancellationToken).ConfigureAwait(true);
            await RefreshPluginsAsync(forceRemoteRefresh: false, cancellationToken).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = "The plugin could not be enabled. Review its current state before retrying.";
            System.Diagnostics.Debug.WriteLine($"[PluginStorePageViewModel] Enable error: {ex}");
            return false;
        }
    }

    public async Task<bool> DisablePluginAsync(PluginCardViewModel card, CancellationToken cancellationToken = default)
    {
        if (card == null || !card.IsInstalled || card.IsBuiltIn) return false;

        ErrorMessage = null;
        try
        {
            await _registry.DisablePluginAsync(card.Id, _host, cancellationToken).ConfigureAwait(true);
            await RefreshPluginsAsync(forceRemoteRefresh: false, cancellationToken).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = "The plugin could not be disabled. Review its current state before retrying.";
            System.Diagnostics.Debug.WriteLine($"[PluginStorePageViewModel] Disable error: {ex}");
            return false;
        }
    }

    public async Task<bool> UninstallPluginAsync(PluginCardViewModel card, CancellationToken cancellationToken = default)
    {
        if (card == null || !card.IsInstalled || card.IsBuiltIn) return false;

        ErrorMessage = null;
        bool wasEnabled = card.InstalledState == PluginState.Enabled;
        bool disabledForUninstall = false;
        try
        {
            if (wasEnabled)
            {
                await _registry.DisablePluginAsync(card.Id, _host, cancellationToken).ConfigureAwait(true);
                disabledForUninstall = true;
            }

            bool removed = await _installerService.UninstallPluginAsync(card.Id, cancellationToken).ConfigureAwait(true);
            if (!removed) throw new InvalidOperationException("The plugin package could not be removed.");

            await _registry.DiscoverAndInitializeAsync(_host, cancellationToken).ConfigureAwait(true);
            await RefreshPluginsAsync(forceRemoteRefresh: false, cancellationToken).ConfigureAwait(true);
            return true;
        }
        catch (OperationCanceledException)
        {
            if (disabledForUninstall) await TryRestoreEnabledStateAsync(card.Id).ConfigureAwait(true);
            throw;
        }
        catch (Exception ex)
        {
            bool restored = !disabledForUninstall || await TryRestoreEnabledStateAsync(card.Id).ConfigureAwait(true);
            ErrorMessage = restored
                ? "Plugin uninstall failed. The plugin was restored to its previous enabled state."
                : "Plugin uninstall failed after the plugin was disabled, and its previous enabled state could not be restored. Review the plugin state before continuing.";
            System.Diagnostics.Debug.WriteLine($"[PluginStorePageViewModel] Uninstall error: {ex}");
            await RefreshPluginsAsync(forceRemoteRefresh: false, CancellationToken.None).ConfigureAwait(true);
            return false;
        }
    }

    private async Task<bool> TryRestoreEnabledStateAsync(string pluginId)
    {
        try
        {
            await _registry.EnablePluginAsync(pluginId, _host, CancellationToken.None).ConfigureAwait(true);
            return _registry.GetAllPlugins().Any(entry =>
                string.Equals(entry.Id, pluginId, StringComparison.OrdinalIgnoreCase) && entry.State == PluginState.Enabled);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PluginStorePageViewModel] Failed to restore plugin '{pluginId}' after uninstall failure: {ex}");
            return false;
        }
    }

    private static bool MatchesCategory(string category, string selectedCategory) =>
        string.Equals(selectedCategory, "All", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(category, selectedCategory, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesSearch(string searchQuery, string id, string name, string description, string author, string category, IEnumerable<string> commands)
    {
        if (string.IsNullOrWhiteSpace(searchQuery)) return true;
        string q = searchQuery.Trim();
        return id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               author.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               commands.Any(command => command.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
    }
}
