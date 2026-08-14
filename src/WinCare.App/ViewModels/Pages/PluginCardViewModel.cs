namespace WinCare.App.ViewModels.Pages;

using WinCare.Application.Plugins;

public sealed class PluginCardViewModel
{
    public PluginCardViewModel(PluginRegistryEntry entry)
    {
        Id = entry.Id;
        Name = entry.Name;
        Version = entry.Version;
        Author = entry.Author;
        Description = entry.Description;
        Category = entry.Category;
        IsBuiltIn = entry.IsBuiltIn;
        State = entry.State;
        CommandCount = entry.Commands.Count;
        ErrorMessage = entry.ErrorMessage;
    }

    public string Id { get; }
    public string Name { get; }
    public string Version { get; }
    public string Author { get; }
    public string Description { get; }
    public string Category { get; }
    public bool IsBuiltIn { get; }
    public PluginState State { get; }
    public int CommandCount { get; }
    public string? ErrorMessage { get; }

    public string StatusBadgeText => State switch
    {
        PluginState.Enabled => IsBuiltIn ? "BUILT-IN" : "ENABLED",
        PluginState.Disabled => "DISABLED",
        PluginState.Error => "ERROR",
        _ => "UNKNOWN"
    };
}
