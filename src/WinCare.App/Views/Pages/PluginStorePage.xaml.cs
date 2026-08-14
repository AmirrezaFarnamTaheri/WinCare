using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using WinCare.App.ViewModels.Pages;
using WinCare.App.Views.Dialogs;

namespace WinCare.App.Views.Pages;

public sealed partial class PluginStorePage : Page
{
    public PluginStorePageViewModel ViewModel { get; }

    public PluginStorePage()
    {
        InitializeComponent();
        ViewModel = new PluginStorePageViewModel();
        Loaded += async (s, e) => await ViewModel.RefreshPluginsAsync();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.SearchQuery = sender.Text;
        }
    }

    private async void CategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string category)
        {
            ViewModel.SelectedCategory = category;

            foreach (var child in CategoryFilterPanel.Children)
            {
                if (child is Button b)
                {
                    b.Style = string.Equals(b.Tag as string, category, StringComparison.OrdinalIgnoreCase)
                        ? (Style)Application.Current.Resources["AccentButtonStyle"]
                        : null;
                }
            }
        }
    }

    private async void DetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is PluginCardViewModel card)
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
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is PluginCardViewModel card)
        {
            await ViewModel.InstallPluginAsync(card);
        }
    }
}
