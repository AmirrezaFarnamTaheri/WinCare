using WinCare.Application.Navigation;

namespace WinCare.App.ViewModels;

public sealed class ShellViewModel
{
    public IReadOnlyList<NavigationDefinition> NavigationItems { get; } = NavigationCatalog.Items;
}
