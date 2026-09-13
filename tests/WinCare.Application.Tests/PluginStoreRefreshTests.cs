using System.Reflection;
using WinCare.App.ViewModels.Pages;
using WinCare.Application.Plugins;

namespace WinCare.Application.Tests;

public sealed class PluginStoreRefreshTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Older_response_cannot_replace_newer_cards_or_trust(bool olderFails)
    {
        var catalog = new ControlledCatalog();
        using var vm = Create(catalog);
        Task older = vm.RefreshPluginsAsync();
        Task newer = vm.RefreshPluginsAsync();
        catalog.Requests[1].SetResult(Catalog("current", true));
        await newer;
        if (olderFails) catalog.Requests[0].SetException(new IOException("Offline"));
        else catalog.Requests[0].SetResult(Catalog("obsolete", false));
        await older;
        Assert.Equal("current", Assert.Single(vm.Plugins).Id);
        Assert.Equal("current", vm.CatalogStatusMessage);
        Assert.True(vm.IsCatalogTrustVerified);
        Assert.False(vm.HasError);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task Retry_clears_connection_error_and_restores_empty_state()
    {
        var catalog = new ControlledCatalog();
        using var vm = Create(catalog);
        Task failed = vm.RefreshPluginsAsync();
        catalog.Requests[0].SetException(new IOException("Offline"));
        await failed;
        Assert.True(vm.HasError);
        Assert.True(vm.HasCatalogError);
        Assert.False(vm.IsEmpty);
        Assert.True(vm.CanRefresh);
        Task retry = vm.RefreshPluginsAsync(forceRemoteRefresh: true);
        Assert.False(vm.CanRefresh);
        catalog.Requests[1].SetResult(new RemotePluginCatalog());
        await retry;
        Assert.True(catalog.LastForceRefresh);
        Assert.False(vm.HasError);
        Assert.False(vm.HasCatalogError);
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public async Task Catalog_refresh_preserves_unrelated_operation_failure()
    {
        var catalog = new ControlledCatalog();
        using var vm = Create(catalog);
        vm.ErrorMessage = "Plugin discovery failed.";
        Task refresh = vm.RefreshPluginsAsync();
        catalog.Requests[0].SetResult(new RemotePluginCatalog());
        await refresh;
        Assert.Equal("Plugin discovery failed.", vm.ErrorMessage);
        Assert.False(vm.HasCatalogError);
    }

    [Fact]
    public async Task Leaving_page_prevents_inflight_response_from_publishing()
    {
        var catalog = new ControlledCatalog();
        var vm = Create(catalog);
        Task pending = vm.RefreshPluginsAsync();
        vm.Dispose();
        catalog.Requests[0].SetResult(Catalog("late", true));
        await pending;
        Assert.Empty(vm.Plugins);
        Assert.False(vm.IsCatalogTrustVerified);
    }

    private static RemotePluginCatalog Catalog(string id, bool trusted) => new()
    {
        TrustStatusMessage = id, IsTrustVerified = trusted,
        Plugins = [new RemotePluginItem { Id = id, Name = id }]
    };

    private static PluginStorePageViewModel Create(ControlledCatalog catalog) => new(
        DispatchProxy.Create<IPluginRegistry, UnusedDependency>(), catalog,
        DispatchProxy.Create<IPluginInstallerService, UnusedDependency>(),
        DispatchProxy.Create<IPluginHost, UnusedDependency>());

    public class UnusedDependency : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name == nameof(IPluginRegistry.GetAllPlugins)
                ? Array.Empty<PluginRegistryEntry>()
                : throw new InvalidOperationException($"Unexpected dependency call: {method?.Name}");
    }

    private sealed class ControlledCatalog : IRemoteCatalogService
    {
        public List<TaskCompletionSource<RemotePluginCatalog>> Requests { get; } = [];
        public bool LastForceRefresh { get; private set; }
        public Task<RemotePluginCatalog> GetCatalogAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            LastForceRefresh = forceRefresh;
            var request = new TaskCompletionSource<RemotePluginCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add(request);
            return request.Task;
        }
        public Task<IReadOnlyList<RemotePluginItem>> SearchPluginsAsync(string query, string? category = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
