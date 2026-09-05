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
        ViewModel = new AiDoctorPageViewModel();
        InitializeComponent();
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
        if (sender is not Button btn || btn.Tag is not WinCare.Application.Diagnostics.ProposedActionStep step)
        {
            return;
        }

        btn.IsEnabled = false;
        try
        {
            var preview = await ViewModel.PreviewStepAsync(step);
            if (preview.Status != WinCare.Domain.Commands.CommandResultStatus.Succeeded)
            {
                btn.Content = preview.Status == WinCare.Domain.Commands.CommandResultStatus.Blocked ? "⚠ Blocked" : "⚠ Failed";
                ToolTipService.SetToolTip(btn, preview.Message);
                return;
            }

            if (step.IsReadOnly)
            {
                btn.Content = "✓ Done";
                ToolTipService.SetToolTip(btn, preview.Message);
                return;
            }

            if (preview.ReviewPlan is null)
            {
                btn.Content = "⚠ Review unavailable";
                ToolTipService.SetToolTip(btn, "The dispatcher did not issue a mutation review receipt. Run the preview again.");
                return;
            }

            string previewData = preview.Data is { } data ? data.ToString() : "No additional structured preview data.";
            var dialog = new ContentDialog
            {
                Title = "Review preview before applying",
                Content = $"Action: {step.Title}\nCommand: {step.CommandId}\nTarget: {step.AffectedResource}\nRisk: {step.RiskLevel}\nAdministrator access: {(step.RequiresElevation ? "Required" : "Not required")}\n\nPreview result:\n{preview.Message}\n\n{previewData}\n\nThis review receipt is single-use and bound to the exact command parameters above.",
                PrimaryButtonText = "Apply reviewed change",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                btn.Content = step.ActionButtonText;
                return;
            }

            var result = await ViewModel.ApplyPreviewedStepAsync(step, preview.ReviewPlan);
            btn.Content = result.Status switch
            {
                WinCare.Domain.Commands.CommandResultStatus.Succeeded => "✓ Done",
                WinCare.Domain.Commands.CommandResultStatus.Blocked => "⚠ Blocked",
                _ => "⚠ Failed",
            };
            ToolTipService.SetToolTip(btn, result.Message);
        }
        catch (Exception ex)
        {
            btn.Content = "Error";
            ToolTipService.SetToolTip(btn, "The step could not be completed. Review Activity for details before retrying.");
            System.Diagnostics.Debug.WriteLine($"[AiDoctorPage] Step execution fault: {ex}");
        }
        finally
        {
            btn.IsEnabled = true;
        }
    }
}
