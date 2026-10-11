namespace WinCare.App.ViewModels.Pages;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinCare.App.Services;
using WinCare.Application.Diagnostics;

public sealed record TroubleshootResult(
    string Heading,
    string Summary,
    DoctorActionPlan? ActionPlan = null
);

/// <summary>Shows only the latest troubleshooting result. User descriptions are never retained in a transcript.</summary>
public sealed class AiDoctorPageViewModel : INotifyPropertyChanged
{

    private readonly IIntentTranslator _intentTranslator;
    private string _userPrompt = string.Empty;
    private bool _isAnalyzing;
    private DoctorActionPlan? _currentPlan;
    private CancellationTokenSource? _activeCts;
    private int _requestVersion;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<TroubleshootResult> Results { get; } = new();
    public bool HasResults => Results.Count > 0;
    public bool IsEmpty => !HasResults && !IsAnalyzing;

    /// <summary>
    /// Cancels an in-flight diagnosis analysis, if any.
    /// </summary>
    public void CancelAnalysis()
    {
        if (_activeCts is null) return;
        StopActiveAnalysis();
        ShowResult(new TroubleshootResult("Check cancelled", "No maintenance action was started."));
    }

    // Navigating away must not leave this cached page busy while a platform
    // probe ignores cancellation. Invalidate the request before signalling it.
    public void ClearSession()
    {
        StopActiveAnalysis();
        UserPrompt = string.Empty;
        ClearResults();
    }

    private void StopActiveAnalysis()
    {
        ++_requestVersion;
        CancellationTokenSource? pending = _activeCts;
        _activeCts = null;
        IsAnalyzing = false;
        CurrentPlan = null;
        pending?.Cancel();
    }

    private void ClearResults()
    {
        Results.Clear();
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void ShowResult(TroubleshootResult result)
    {
        Results.Clear();
        Results.Add(result);
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(IsEmpty));
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
            OnPropertyChanged(nameof(IsEmpty));
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
            ShowResult(new TroubleshootResult("Description too long", "Use 500 characters or fewer."));
            return;
        }

        int version = ++_requestVersion;
        UserPrompt = string.Empty;
        ClearResults();

        using CancellationTokenSource linkedCts = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
        _activeCts = linkedCts;
        IsAnalyzing = true;
        CurrentPlan = null;
        try
        {
            DoctorActionPlan plan = await _intentTranslator.TranslateAsync(prompt, linkedCts.Token);
            linkedCts.Token.ThrowIfCancellationRequested();
            if (version != _requestVersion) return; // Page was left while the check was running.
            CurrentPlan = plan;

            string findings = plan.Findings.Count == 1 ? "1 finding" : $"{plan.Findings.Count} findings";
            string steps = plan.ProposedSteps.Count == 1 ? "1 tool to review" : $"{plan.ProposedSteps.Count} tools to review";
            string evidence = plan.MeasuredEvidence.Count == 0
                ? "No local readings available for this result."
                : $"Used {plan.MeasuredEvidence.Count} local reading{(plan.MeasuredEvidence.Count == 1 ? string.Empty : "s")}.";
            string responseText = $"{plan.DiagnosisSummary}\n\n{evidence} {findings} · {steps}.";
            ShowResult(new TroubleshootResult("Recommended checks", responseText, plan));
        }
        catch (OperationCanceledException)
        {
            if (version == _requestVersion)
                ShowResult(new TroubleshootResult("Check cancelled", "No maintenance action was started."));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Troubleshoot] Check failed ({ex.GetType().Name}).");
            if (version == _requestVersion)
                ShowResult(new TroubleshootResult(
                    "Check unavailable",
                    "This check could not finish. Try another problem or open Power tools."));
        }
        finally
        {
            // The cancelled task may finish after navigation has started a new
            // check. Only the task that owns the current token may clear the UI.
            if (ReferenceEquals(_activeCts, linkedCts))
            {
                _activeCts = null;
                IsAnalyzing = false;
            }
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
