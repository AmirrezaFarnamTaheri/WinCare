using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

using WinCare.App.ViewModels.Pages;
using WinCare.App.Views.Dialogs;

namespace WinCare.App.Views.Pages;

public sealed partial class PluginStorePage : Page
{
    private bool _initialized;
    public PluginStorePageViewModel ViewModel { get; }

    public static Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public PluginStorePage()
    {
        ViewModel = new PluginStorePageViewModel();
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            try
            {
                await ViewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PluginStorePage] Initialize failed: {ex.Message}");
            }
        };
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.SearchQuery = sender.Text;
        }
    }

    private async void DetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is PluginCardViewModel card)
        {
            await ShowPluginDetailsDialogAsync(card);
        }
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is PluginCardViewModel card)
        {
            // Gate installation behind the capability and trust consent dialog
            await ShowPluginDetailsDialogAsync(card);
        }
    }

    private async void EnableButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is PluginCardViewModel card)
        {
            await ViewModel.EnablePluginAsync(card);
        }
    }

    private async void DisableButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is PluginCardViewModel card)
        {
            await ViewModel.DisablePluginAsync(card);
        }
    }

    private async void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is PluginCardViewModel card)
        {
            await ViewModel.UninstallPluginAsync(card);
        }
    }

    private async Task ShowPluginDetailsDialogAsync(PluginCardViewModel card)
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

        var dialog = new PluginDetailDialog(item)
        {
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && card.CanInstall)
        {
            await ViewModel.InstallPluginAsync(card);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.Dispose();
        base.OnNavigatedFrom(e);
    }
}
