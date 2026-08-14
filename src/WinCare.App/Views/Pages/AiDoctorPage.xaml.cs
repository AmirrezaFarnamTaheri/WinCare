using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinCare.App.ViewModels.Pages;

namespace WinCare.App.Views.Pages;

public sealed partial class AiDoctorPage : Page
{
    public AiDoctorPageViewModel ViewModel { get; }

    public AiDoctorPage()
    {
        InitializeComponent();
        ViewModel = new AiDoctorPageViewModel();
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SubmitPromptAsync();
        ChatScrollViewer?.ChangeView(null, ChatScrollViewer.ScrollableHeight, null);
    }

    private async void PromptTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            await ViewModel.SubmitPromptAsync();
            ChatScrollViewer?.ChangeView(null, ChatScrollViewer.ScrollableHeight, null);
        }
    }

    private async void ExecuteStepButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is WinCare.Application.Diagnostics.ProposedActionStep step)
        {
            btn.IsEnabled = false;
            try
            {
                var result = await ViewModel.ExecuteStepAsync(step);
                btn.Content = result.Status == WinCare.Domain.Commands.CommandResultStatus.Succeeded ? "✓ Done" : "⚠ Failed";
            }
            catch (Exception ex)
            {
                btn.Content = "Error";
                System.Diagnostics.Debug.WriteLine($"[AiDoctorPage] Step execution fault: {ex.Message}");
            }
        }
    }
}
