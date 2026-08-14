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
            var isReadOnly = step.IsReadOnly;
            bool userApproved = isReadOnly;

            if (!isReadOnly)
            {
                var dialog = new ContentDialog
                {
                    Title = "Review & Approve Remediation Action",
                    Content = $"Action: {step.Title}\nCommand: {step.CommandId}\nTarget: {step.AffectedResource}\nRisk Level: {step.RiskLevel}\nAdministrator Access: {(step.RequiresElevation ? "Required" : "Not required")}\n\nDo you want to approve and execute this modification?",
                    PrimaryButtonText = "Approve & Execute",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };

                var dialogResult = await dialog.ShowAsync();
                if (dialogResult != ContentDialogResult.Primary)
                {
                    return;
                }
                userApproved = true;
            }

            btn.IsEnabled = false;
            try
            {
                var result = await ViewModel.ExecuteStepAsync(step, userApproved: userApproved);
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
