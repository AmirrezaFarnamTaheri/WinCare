namespace WinCare.App.ViewModels.Pages;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinCare.App.Services;
using WinCare.Application.Diagnostics;

public sealed record DoctorChatMessage(
    string Sender,
    string Text,
    bool IsUser,
    DateTime TimestampUtc,
    DoctorActionPlan? ActionPlan = null
);

/// <summary>
/// Owns local diagnostic conversation state only. Suggested actions are handed to the
/// canonical Power tools inspector, which owns preview, approval, execution, and receipts.
/// </summary>
public sealed class AiDoctorPageViewModel : INotifyPropertyChanged
{
    private readonly IIntentTranslator _intentTranslator;
    private string _userPrompt = string.Empty;
    private bool _isAnalyzing;
    private DoctorActionPlan? _currentPlan;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DoctorChatMessage> Messages { get; } = new();

    public string UserPrompt
    {
        get => _userPrompt;
        set
        {
            if (_userPrompt == value) return;
            _userPrompt = value;
            OnPropertyChanged();
        }
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (_isAnalyzing == value) return;
            _isAnalyzing = value;
            OnPropertyChanged();
        }
    }

    public DoctorActionPlan? CurrentPlan
    {
        get => _currentPlan;
        private set
        {
            if (_currentPlan == value) return;
            _currentPlan = value;
            OnPropertyChanged();
        }
    }

    public AiDoctorPageViewModel(IIntentTranslator? intentTranslator = null)
    {
        var inferenceEngine = new RuleBasedIntentInferenceEngine();
        _intentTranslator = intentTranslator ?? new IntentTranslator(inferenceEngine, AppRuntime.Current.ToolCatalog);

        Messages.Add(new DoctorChatMessage(
            "WinCare",
            "Describe a Windows problem such as storage pressure, high memory use, lag, or network trouble. WinCare uses local rules and measured evidence to suggest relevant checks and reviewable next steps.",
            IsUser: false,
            DateTime.UtcNow));
    }

    public async Task SubmitPromptAsync(CancellationToken cancellationToken = default)
    {
        string prompt = UserPrompt?.Trim() ?? string.Empty;
        if (prompt.Length == 0 || IsAnalyzing) return;

        UserPrompt = string.Empty;
        Messages.Add(new DoctorChatMessage("You", prompt, IsUser: true, DateTime.UtcNow));

        IsAnalyzing = true;
        CurrentPlan = null;
        try
        {
            DoctorActionPlan plan = await _intentTranslator.TranslateAsync(prompt, cancellationToken);
            CurrentPlan = plan;

            string responseText = $"{plan.DiagnosisSummary}\n\n" +
                $"Evidence collected: {plan.MeasuredEvidence.Count} measured probe{(plan.MeasuredEvidence.Count == 1 ? string.Empty : "s")}.\n" +
                $"Findings: {plan.Findings.Count}. Suggested next steps: {plan.ProposedSteps.Count}.\n" +
                "Open a suggested step to review it in Power tools before anything can change Windows.";
            Messages.Add(new DoctorChatMessage("WinCare", responseText, IsUser: false, DateTime.UtcNow, plan));
        }
        catch (OperationCanceledException)
        {
            Messages.Add(new DoctorChatMessage("WinCare", "Analysis cancelled.", IsUser: false, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Troubleshoot] Analysis fault: {ex}");
            Messages.Add(new DoctorChatMessage(
                "WinCare",
                "Diagnosis could not be completed. No change was applied. Review Activity or the WinCare logs if the problem continues.",
                IsUser: false,
                DateTime.UtcNow));
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
