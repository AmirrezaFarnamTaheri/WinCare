using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinCare.App.Views.Dialogs;

public sealed partial class FirstRunTourDialog : ContentDialog
{
    private const int LastStep = 2;
    private int _step;

    public FirstRunTourDialog()
    {
        InitializeComponent();
        ShowStep();
    }

    private void NextButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_step == LastStep) return;

        args.Cancel = true;
        _step++;
        ShowStep();
    }

    private void ShowStep()
    {
        CheckupStep.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        CareStep.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        FindStep.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        StepCounter.Text = $"{_step + 1} of {LastStep + 1}";
        PrimaryButtonText = _step == LastStep ? "Done" : "Next";
    }
}
