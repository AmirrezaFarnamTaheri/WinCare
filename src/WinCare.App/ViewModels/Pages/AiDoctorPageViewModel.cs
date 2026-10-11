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
    private CancellationTokenSource? _activeCts;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<DoctorChatMessage> Messages { get; } = new();
    public bool HasMessages => Messages.Count > 0;
    public bool IsEmpty => !HasMessages;

    /// <summary>
    /// Cancels an in-flight diagnosis analysis, if any.
    /// </summary>
    public void CancelAnalysis()
    {
        if (_activeCts is null || !_isAnalyzing) return;
        _activeCts.Cancel();
    }

    private void AddMessage(DoctorChatMessage message)
    {
        Messages.Add(message);
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(IsEmpty));
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
        _intentTranslator = intentTranslator ??
            new IntentTranslator(new RuleBasedIntentInferenceEngine(), AppRuntime.Current.ToolCatalog);
    }

    public async Task SubmitPromptAsync(CancellationToken cancellationToken = default)
    {
        string prompt = UserPrompt.Trim();
        if (prompt.Length == 0 || IsAnalyzing) return;
        if (prompt.Length > 500)
        {
            AddMessage(new DoctorChatMessage("Input", "Keep the description under 500 characters.", false, DateTime.UtcNow));
            return;
        }

        UserPrompt = string.Empty;
        AddMessage(new DoctorChatMessage("Problem", prompt, IsUser: true, DateTime.UtcNow));

        using CancellationTokenSource linkedCts = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
        _activeCts = linkedCts;
        IsAnalyzing = true;
        CurrentPlan = null;
        try
        {
            DoctorActionPlan plan = await _intentTranslator.TranslateAsync(prompt, linkedCts.Token);
            CurrentPlan = plan;

            string findings = plan.Findings.Count == 1 ? "1 finding" : $"{plan.Findings.Count} findings";
            string steps = plan.ProposedSteps.Count == 1 ? "1 tool to review" : $"{plan.ProposedSteps.Count} tools to review";
            string evidence = plan.MeasuredEvidence.Count == 0
                ? "No local readings available for this result."
                : $"Used {plan.MeasuredEvidence.Count} local reading{(plan.MeasuredEvidence.Count == 1 ? string.Empty : "s")}.";
            string responseText = $"{plan.DiagnosisSummary}\n\n{evidence} {findings} · {steps}.";
            AddMessage(new DoctorChatMessage("Suggestions", responseText, IsUser: false, DateTime.UtcNow, plan));
        }
        catch (OperationCanceledException)
        {
            AddMessage(new DoctorChatMessage("Status", "Check cancelled.", IsUser: false, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Troubleshoot] Check failed: {ex}");
            AddMessage(new DoctorChatMessage(
                "Status",
                "That check could not finish. Try another description or open a relevant tool manually.",
                IsUser: false,
                DateTime.UtcNow));
        }
        finally
        {
            _activeCts = null;
            IsAnalyzing = false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
