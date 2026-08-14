namespace WinCare.App.ViewModels.Pages;

using System.Collections.ObjectModel;
using WinCare.Application.Plugins;

public sealed class PluginStorePageViewModel
{
    private readonly IPluginRegistry _registry;

    public PluginStorePageViewModel(IPluginRegistry? registry = null)
    {
        _registry = registry ?? new PluginRegistryService();
        Plugins = new ObservableCollection<PluginCardViewModel>();
        RefreshPlugins();
    }

    public ObservableCollection<PluginCardViewModel> Plugins { get; }

    public void RefreshPlugins()
    {
        Plugins.Clear();
        var allPlugins = _registry.GetAllPlugins();
        foreach (var plugin in allPlugins)
        {
            Plugins.Add(new PluginCardViewModel(plugin));
        }
    }
}
