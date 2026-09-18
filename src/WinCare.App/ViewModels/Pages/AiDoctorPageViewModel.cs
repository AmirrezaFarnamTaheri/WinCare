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

/// <summary>Holds the Troubleshoot conversation and passes chosen steps to Power tools.</summary>
public sealed class AiDoctorPageViewModel : INotifyPropertyChanged
{
    /// <summary>The Troubleshoot page is cached for the session, so the conversation is capped;
    /// plan steps stay re-derivable from <see cref="CurrentPlan"/>.</summary>
    private const int MaxKeptMessages = 100;

    private readonly IIntentTranslator _intentTranslator;
    private string _userPrompt = string.Empty;
    private bool _isAnalyzing;
    private DoctorActionPlan? _currentPlan;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<DoctorChatMessage> Messages { get; } = new();

    private void AddMessage(DoctorChatMessage message)
    {
        Messages.Add(message);
        while (Messages.Count > MaxKeptMessages) Messages.RemoveAt(0);
    }

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

        AddMessage(new DoctorChatMessage(
            "WinCare",
            "Tell me what's wrong — for example, low disk space, high memory use, lag, or network trouble. I'll check local Windows signals and suggest a few next steps.",
            IsUser: false,
            DateTime.UtcNow));
    }

    public async Task SubmitPromptAsync(CancellationToken cancellationToken = default)
    {
        string prompt = UserPrompt.Trim();
        if (prompt.Length == 0 || IsAnalyzing) return;

        UserPrompt = string.Empty;
        AddMessage(new DoctorChatMessage("You", prompt, IsUser: true, DateTime.UtcNow));

        IsAnalyzing = true;
        CurrentPlan = null;
        try
        {
            DoctorActionPlan plan = await _intentTranslator.TranslateAsync(prompt, cancellationToken);
            CurrentPlan = plan;

            string signalCount = $"{plan.MeasuredEvidence.Count} local signal{(plan.MeasuredEvidence.Count == 1 ? string.Empty : "s")}";
            string findingCount = $"{plan.Findings.Count} item{(plan.Findings.Count == 1 ? string.Empty : "s")}";
            string stepCount = $"{plan.ProposedSteps.Count} next step{(plan.ProposedSteps.Count == 1 ? string.Empty : "s")}";
            string responseText = $"{plan.DiagnosisSummary}\n\nI checked {signalCount} and found {findingCount}. {stepCount} ready to review.";
            AddMessage(new DoctorChatMessage("WinCare", responseText, IsUser: false, DateTime.UtcNow, plan));
        }
        catch (OperationCanceledException)
        {
            AddMessage(new DoctorChatMessage("WinCare", "Check cancelled.", IsUser: false, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Troubleshoot] Check failed: {ex}");
            AddMessage(new DoctorChatMessage(
                "WinCare",
                "I couldn't finish that check. Nothing was changed. Try again, or open Activity if the problem keeps happening.",
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
