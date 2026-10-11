using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using WinCare.App.ViewModels.Pages;
using WinCare.App.Views;
using WinCare.Application.Diagnostics;

namespace WinCare.App.Views.Pages;

public sealed partial class AiDoctorPage : Page
{
    // Kept as a one-line delegation to the shared LayoutVisibility helper so the bool-to-
    // visibility pair has a single implementation; this wrapper exists for the XAML contract.
    public static Visibility BoolToVisibility(bool value) => LayoutVisibility.BoolToVisibility(value);
    public static Visibility PlanToVisibility(DoctorActionPlan? plan) =>
        plan is null ? Visibility.Collapsed : Visibility.Visible;

    public AiDoctorPageViewModel ViewModel { get; }

    public AiDoctorPage()
    {
        ViewModel = new AiDoctorPageViewModel();
        InitializeComponent();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.ClearSession();
        base.OnNavigatedFrom(e);
    }

    private async void QuickIssue_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string problem } || ViewModel.IsAnalyzing) return;
        ViewModel.UserPrompt = problem;
        await ViewModel.SubmitPromptAsync();
        ScrollToResults();
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SubmitPromptAsync();
        ScrollToResults();
    }

    private async void PromptTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        await ViewModel.SubmitPromptAsync();
        ScrollToResults();
    }

    private void CancelCheck_Click(object sender, RoutedEventArgs e) => ViewModel.CancelAnalysis();

    private void ScrollToResults()
    {
        try
        {
            if (IsLoaded) ResultsScrollViewer?.ChangeView(null, ResultsScrollViewer.ScrollableHeight, null);
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
    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < 720;
        TroubleshootLayout.Padding = compact ? new Thickness(16,18,16,20) : new Thickness(32,28,32,28);
        for (int i = 0; i < 3; i++)
            QuickIssuesGrid.ColumnDefinitions[i].Width = compact
                ? i == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(0)
                : new GridLength(1, GridUnitType.Star);
        Button[] choices = [StorageIssue, MemoryIssue, NetworkIssue, UpdateIssue, PrivacyIssue];
        for (int i = 0; i < choices.Length; i++)
        {
            Grid.SetRow(choices[i], compact ? i : i / 3);
            Grid.SetColumn(choices[i], compact ? 0 : i % 3);
        }
        Grid.SetRow(FindStepsButton, compact ? 1 : 0);
        Grid.SetColumn(FindStepsButton, compact ? 0 : 1);
        Grid.SetColumnSpan(FindStepsButton, compact ? 2 : 1);
        FindStepsButton.HorizontalAlignment = compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        Grid.SetRow(ProblemHint, compact ? 2 : 1);
    }


}
