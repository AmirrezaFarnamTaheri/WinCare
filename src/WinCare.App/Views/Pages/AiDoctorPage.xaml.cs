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
    // Kept as a one-line delegation to the shared LayoutVisibility helper so the bool-to-
    // visibility pair has a single implementation; this wrapper exists for the XAML contract.
    public static Visibility BoolToVisibility(bool value) => LayoutVisibility.BoolToVisibility(value);
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
        ScrollToLatestMessage();
    }

    private async void PromptTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        await ViewModel.SubmitPromptAsync();
        ScrollToLatestMessage();
    }

    private void ScrollToLatestMessage()
    {
        try
        {
            if (IsLoaded) ChatScrollViewer?.ChangeView(null, ChatScrollViewer.ScrollableHeight, null);
        }
        catch (Exception ex)
        {
            // Preserve the completed answer if the view was detached before scrolling.
            System.Diagnostics.Debug.WriteLine($"[AiDoctorPage] Scroll failed: {ex}");
        }
    }

    private void ExecuteStepButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProposedActionStep step }) return;

        JsonElement parameters = step.Parameters is { Count: > 0 }
            ? JsonSerializer.SerializeToElement(step.Parameters)
            : JsonSerializer.SerializeToElement(new { });
        PageNavigation.OpenTool(this, step.CommandId, parameters);
    }
}
