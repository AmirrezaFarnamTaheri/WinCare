using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

using WinCare.App.ViewModels.Pages;
using WinCare.App.Views;
using WinCare.App.Views.Dialogs;

namespace WinCare.App.Views.Pages;

public sealed partial class PluginStorePage : Page
{
    private bool _initialized;
    public PluginStorePageViewModel ViewModel { get; }

    // Kept as a one-line delegation to the shared LayoutVisibility helper so the bool-to-
    // visibility pair has a single implementation; this wrapper exists for the XAML contract.
    public static Visibility BoolToVisibility(bool value) => LayoutVisibility.BoolToVisibility(value);

    public PluginStorePage()
    {
        var runtime = WinCare.App.Services.AppRuntime.Current;
        ViewModel = new PluginStorePageViewModel(runtime.PluginRegistry, runtime.CatalogService,
            runtime.InstallerService, runtime.PluginHost, runtime.InitializePluginsAsync);
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (_initialized) return;

            _initialized = true;
            try
            {
                await ViewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PluginStorePage] Initialize failed: {ex}");
            }
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        // Cached page: Dispose() runs on every leave, so undo it here or the second visit
        // reuses a disposed view model whose search and filtering silently no-op.
        ViewModel.Reactivate();
        base.OnNavigatedTo(e);
        if (e.Parameter is string query && !string.IsNullOrWhiteSpace(query))
        {
            PluginSearchBox.Text = query;
            ViewModel.SearchQuery = query;
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            ViewModel.SearchQuery = sender.Text;
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = LayoutVisibility.IsCompact(e.NewSize.Width);
        Grid.SetRow(PluginSearchBox, compact ? 1 : 0);
        Grid.SetColumn(PluginSearchBox, compact ? 0 : 1);
        Grid.SetColumnSpan(PluginSearchBox, compact ? 2 : 1);
        PluginSearchBox.Width = compact ? double.NaN : 280;
        PluginSearchBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(CategoryFilter, compact ? 2 : 1);
    }

    private async void RetryCatalog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.RefreshPluginsAsync(forceRemoteRefresh: true);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            ViewModel.ErrorMessage = "The catalog could not be refreshed. Check your connection and try again.";
        }
    }

    private async void DetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is PluginCardViewModel card)
            await ShowPluginDetailsDialogAsync(card, allowInstall: false);
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is PluginCardViewModel card)
            await ShowPluginDetailsDialogAsync(card, allowInstall: true);
    }

    private async void EnableButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is PluginCardViewModel card)
            await ViewModel.EnablePluginAsync(card);
    }

    private async void DisableButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is PluginCardViewModel card)
            await ViewModel.DisablePluginAsync(card);
    }

    private async void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PluginCardViewModel card }) return;
        try
        {
            var dialog = new ContentDialog
            {
                Title = $"Uninstall {card.Name}?",
                Content = "This removes the extension package and its registered commands. WinCare will disable an enabled extension first and restore it if removal fails.",
                PrimaryButtonText = "Uninstall",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await ViewModel.UninstallPluginAsync(card);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PluginStorePage] Uninstall dialog failed: {ex}");
            ViewModel.ErrorMessage = "The uninstall request couldn't finish. Check the extension's current state before trying again.";
        }
    }

    private async Task ShowPluginDetailsDialogAsync(PluginCardViewModel card, bool allowInstall)
    {
        // Guard the UI interaction itself: the view model methods catch their own failures,
        // but an exception while building or showing the dialog would otherwise escape the
        // async-void click handler and terminate the process. Surface it as a normal error.
        try
        {
            var item = card.RemoteItem ?? new WinCare.Application.Plugins.RemotePluginItem
            {
                Id = card.Id,
                Name = card.Name,
                Author = card.Author,
                Version = card.Version,
                Description = card.Description,
                Category = card.Category,
                Permissions = card.Permissions
            };

            var dialog = new PluginDetailDialog(item, allowInstall)
            {
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (allowInstall && result == ContentDialogResult.Primary && card.CanInstall)
                await ViewModel.InstallPluginAsync(card, card.Permissions);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PluginStorePage] Details failed: {ex}");
            ViewModel.ErrorMessage = "Extension details couldn't be opened. Try again, and check Activity if it keeps happening.";
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.Dispose();
        base.OnNavigatedFrom(e);
    }
}
