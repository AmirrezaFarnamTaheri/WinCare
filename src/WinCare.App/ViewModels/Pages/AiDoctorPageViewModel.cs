namespace WinCare.App.ViewModels.Pages;

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using WinCare.App.Services;
using WinCare.Application.Commands;
using WinCare.Application.Diagnostics;
using WinCare.Application.Tools;
using WinCare.Domain.Commands;

public sealed record DoctorChatMessage(
    string Sender,
    string Text,
    bool IsUser,
    DateTime TimestampUtc,
    DoctorActionPlan? ActionPlan = null
);

public sealed class AiDoctorPageViewModel : INotifyPropertyChanged
{
    private readonly IIntentTranslator _intentTranslator;
    private readonly ICommandDispatcher _commandDispatcher;
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
            if (_userPrompt != value)
            {
                _userPrompt = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (_isAnalyzing != value)
            {
                _isAnalyzing = value;
                OnPropertyChanged();
            }
        }
    }

    public DoctorActionPlan? CurrentPlan
    {
        get => _currentPlan;
        private set
        {
            if (_currentPlan != value)
            {
                _currentPlan = value;
                OnPropertyChanged();
            }
        }
    }

    public AiDoctorPageViewModel(
        IIntentTranslator? intentTranslator = null,
        ICommandDispatcher? commandDispatcher = null)
    {
        var catalog = new ToolCatalogService(AppRuntime.Current.PluginRegistry);
        var modelManager = new ModelManager();
        var inferenceEngine = new OnnxInferenceEngine(modelManager);

        _intentTranslator = intentTranslator ?? new IntentTranslator(inferenceEngine, catalog);
        _commandDispatcher = commandDispatcher ?? AppRuntime.Current.Dispatcher;

        // Greeting message
        Messages.Add(new DoctorChatMessage(
            "AI System Doctor",
            "Hello! I am your on-device WinCare AI System Doctor. Describe any issue with your PC (e.g. storage full, high RAM, lag, network ping) and I will diagnose it and generate a safe, verifiable action plan.",
            IsUser: false,
            DateTime.UtcNow
        ));
    }

    public async Task SubmitPromptAsync(CancellationToken cancellationToken = default)
    {
        var prompt = UserPrompt?.Trim();
        if (string.IsNullOrWhiteSpace(prompt) || IsAnalyzing) return;

        UserPrompt = string.Empty;
        Messages.Add(new DoctorChatMessage("You", prompt, IsUser: true, DateTime.UtcNow));

        IsAnalyzing = true;
        try
        {
            var plan = await _intentTranslator.TranslateAsync(prompt, cancellationToken);
            CurrentPlan = plan;

            var responseText = $"**Diagnosis:** {plan.DiagnosisSummary}\n\nIdentified {plan.Findings.Count} findings and {plan.ProposedSteps.Count} recommended remediation steps.";
            Messages.Add(new DoctorChatMessage("AI System Doctor", responseText, IsUser: false, DateTime.UtcNow, plan));
        }
        catch (Exception ex)
        {
            Messages.Add(new DoctorChatMessage(
                "AI System Doctor",
                $"An error occurred while analyzing: {ex.Message}",
                IsUser: false,
                DateTime.UtcNow
            ));
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    public async Task<CommandResult> ExecuteStepAsync(ProposedActionStep step, CancellationToken cancellationToken = default)
    {
        if (step == null)
        {
            throw new ArgumentNullException(nameof(step));
        }

        var emptyParams = System.Text.Json.JsonSerializer.SerializeToElement(new { });
        var correlationId = Guid.NewGuid();
        var approval = step.RiskLevel != CommandCatalog.Models.CommandRisk.ReadOnly
            ? ApprovedMutationPlan.Create(step.CommandId, emptyParams, correlationId)
            : null;

        var request = new CommandRequest(
            CommandId: step.CommandId,
            Parameters: emptyParams,
            Apply: true,
            CorrelationId: correlationId,
            Approval: approval
        );

        return await _commandDispatcher.ExecuteAsync(request, CommandExecutionOptions.Default, cancellationToken);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
