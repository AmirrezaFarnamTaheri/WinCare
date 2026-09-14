using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinCare.App.ViewModels.Pages;
using WinCare.App.Views;
using WinCare.Application.Diagnostics;

namespace WinCare.App.Views.Pages;

public sealed partial class AiDoctorPage : Page
{
    public static Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static HorizontalAlignment UserToHorizontalAlignment(bool isUser) => isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;

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

    /// <summary>Handles the prompt text box key down event.</summary>
    private async void PromptTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        await ViewModel.SubmitPromptAsync();
        ChatScrollViewer?.ChangeView(null, ChatScrollViewer.ScrollableHeight, null);
    }

    /// <summary>Handles the execute step button click event.</summary>
    private void ExecuteStepButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProposedActionStep step }) return;

        JsonElement parameters = step.Parameters is { Count: > 0 }
            ? JsonSerializer.SerializeToElement(step.Parameters)
            : JsonSerializer.SerializeToElement(new { });
        PageNavigation.OpenTool(this, step.CommandId, parameters);
    }
}
