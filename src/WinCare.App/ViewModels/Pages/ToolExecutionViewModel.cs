using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;
using WinCare.CommandCatalog.Models;

namespace WinCare.App.ViewModels.Pages;

public sealed class ToolExecutionViewModel : ObservableObject
{
    private const int MaxParameterJsonCharacters = 1024 * 1024;

    private static readonly JsonSerializerOptions ResultJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly CommandDispatcher _dispatcher;
    private readonly Action<string> _recordRecent;
    private ToolRowViewModel? _selectedTool;
    private bool _isExecuting;
    private bool _isExecutionResultOpen;
    private bool _hasSuccessfulPreview;
    private CommandResultStatus? _executionStatus;
    private string _executionMessage = string.Empty;
    private string _executionResultText = string.Empty;
    private bool _isReviewApproved;
    private string _parameterJson = "{}";
    private ApprovedMutationPlan? _lastApprovedPlan;
    private long _reviewVersion;

    private CancellationTokenSource? _activeCts;

    public ToolExecutionViewModel(CommandDispatcher dispatcher, Action<string> recordRecent)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _recordRecent = recordRecent ?? throw new ArgumentNullException(nameof(recordRecent));
        ExecuteSelectedToolCommand = new AsyncRelayCommand(
            ExecuteSelectedToolAsync,
            CanExecuteSelectedTool);
        CancelSelectedToolCommand = new RelayCommand(CancelSelectedTool);
    }

    public IAsyncRelayCommand ExecuteSelectedToolCommand { get; }
    public IRelayCommand CancelSelectedToolCommand { get; }

    private void CancelSelectedTool()
    {
        _activeCts?.Cancel();
    }

    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            if (SetProperty(ref _isExecuting, value))
            {
                NotifyAvailabilityChanged();
            }
        }
    }

    public bool IsExecutionResultOpen
    {
        get => _isExecutionResultOpen;
        set => SetProperty(ref _isExecutionResultOpen, value);
    }

    public bool CanRunSelectedTool => !IsExecuting &&
        (_selectedTool?.Definition.MigrationStatus is
            MigrationStatus.Implemented or
            MigrationStatus.BehaviorVerified);

    public string PrimaryActionLabel => IsExecuting
        ? "Running"
        : IsMutatingTool && IsReviewApproved
            ? "Apply changes"
            : IsMutatingTool
                ? "Review changes"
                : "Run tool";

    public bool HasExecutionResult => _executionStatus is not null;

    public bool IsExecutionSuccess => _executionStatus == CommandResultStatus.Succeeded;

    public bool IsExecutionError => _executionStatus is
        CommandResultStatus.Blocked or
        CommandResultStatus.Failed or
        CommandResultStatus.NotMigrated;

    public bool IsExecutionCancelled => _executionStatus == CommandResultStatus.Cancelled;

    public string ExecutionTitle => _executionStatus switch
    {
        CommandResultStatus.Succeeded => "Completed",
        CommandResultStatus.Cancelled => "Cancelled",
        CommandResultStatus.NotMigrated => "Not available yet",
        CommandResultStatus.Blocked => "Blocked",
        CommandResultStatus.Failed => "Could not complete",
        _ => string.Empty,
    };

    public string ExecutionMessage => _executionMessage;

    public string ExecutionResultText => _executionResultText;

    /// <summary>JSON object passed to the selected command. Kept as text so every catalog command can expose its native contract without bespoke UI code.</summary>
    public string ParameterJson
    {
        get => _parameterJson;
        set
        {
            if (SetProperty(ref _parameterJson, value ?? "{}"))
            {
                ResetReviewState();
            }
        }
    }

    public bool IsMutatingTool => _selectedTool?.Definition.ReadOnly == false;

    public bool CanApproveReview => IsMutatingTool && _hasSuccessfulPreview && !IsExecuting;

    public bool IsReviewApproved
    {
        get => _isReviewApproved;
        set
        {
            bool next = value && CanApproveReview;
            if (SetProperty(ref _isReviewApproved, next))
            {
                OnPropertyChanged(nameof(PrimaryActionLabel));
            }
        }
    }

    public void SelectTool(ToolRowViewModel? tool)
    {
        if (ReferenceEquals(_selectedTool, tool))
        {
            return;
        }

        _selectedTool = tool;
        ResetReviewState();
        ClearExecutionResult();
        NotifyAvailabilityChanged();
    }

    private bool CanExecuteSelectedTool() => CanRunSelectedTool;

    private async Task ExecuteSelectedToolAsync(CancellationToken cancellationToken)
    {
        ToolRowViewModel? selected = _selectedTool;
        if (selected is null || !CanRunSelectedTool)
        {
            return;
        }

        bool apply = IsMutatingTool && IsReviewApproved;
        long reviewVersion = _reviewVersion;
        if (apply && !CanApproveReview)
        {
            return;
        }

        IsExecuting = true;
        ClearExecutionResult();
        try
        {
            string parameterText = string.IsNullOrWhiteSpace(ParameterJson) ? "{}" : ParameterJson;
            if (parameterText.Length > MaxParameterJsonCharacters)
            {
                SetParameterError("Command parameter JSON exceeds the 1 MiB safety limit.");
                return;
            }

            JsonElement parameters;
            try
            {
                using JsonDocument document = JsonDocument.Parse(parameterText);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    SetParameterError("Command parameters must be a JSON object.");
                    return;
                }
                parameters = document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                SetParameterError($"Invalid parameter JSON: {ex.Message}");
                return;
            }

            ApprovedMutationPlan? approval = apply ? _lastApprovedPlan : null;
            CommandRequest request = apply
                ? CommandRequest.Execute(selected.Id, parameters, approval)
                : CommandRequest.Preview(selected.Id, parameters);
            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeCts = linkedCts;
            CommandResult result;
            try
            {
                result = await _dispatcher.ExecuteAsync(
                    request,
                    new CommandExecutionOptions(
                        ReviewApproved: apply,
                        Deadline: DateTimeOffset.UtcNow + ExecutionBudget(selected.Definition)),
                    linkedCts.Token);
            }
            finally
            {
                _activeCts = null;
            }

            _recordRecent(result.CommandId);
            if (reviewVersion == _reviewVersion && ReferenceEquals(_selectedTool, selected))
            {
                ApplyExecutionResult(result);
                if (IsMutatingTool)
                {
                    if (!apply)
                    {
                        bool previewSuccess = result.Status == CommandResultStatus.Succeeded;
                        SetSuccessfulPreview(previewSuccess);
                        _lastApprovedPlan = previewSuccess ? result.ReviewPlan : null;
                    }
                    else
                    {
                        ResetReviewState();
                        _lastApprovedPlan = null;
                    }
                }
            }
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private static TimeSpan ExecutionBudget(CommandDefinition definition) => definition.Id switch
    {
        // Network transfers can legitimately run for a long time while remaining cancellation-aware.
        "download-start" or "download-batch" or "download-start-due" or "steam-backup" or "steam-restore"
            => TimeSpan.FromHours(2),

        // Windows servicing and update APIs regularly exceed interactive command timeouts.
        "wua-download" or "wua-install" or "wua-uninstall" or
        "offline-appx-selection" or "offline-driver-add" or "offline-driver-remove" or
        "offline-package-add" or "offline-feature-set" or "offline-reduction-apply" or
        "provisioning-plan" or "appx-selection" or "hardening-apply"
            => TimeSpan.FromHours(1),

        // Mutations stay bounded but get enough time for native tools and filesystem work.
        _ when !definition.ReadOnly => TimeSpan.FromMinutes(15),

        // Read-only diagnostics should be responsive while allowing large inventories/event queries.
        _ => TimeSpan.FromMinutes(2),
    };

    private void SetParameterError(string message)
    {
        _executionStatus = CommandResultStatus.Blocked;
        _executionMessage = message;
        _executionResultText = JsonSerializer.Serialize(new
        {
            status = CommandResultStatus.Blocked,
            code = "command.parameters_json_invalid",
            message,
        }, ResultJsonOptions);
        IsExecutionResultOpen = true;
        NotifyExecutionResultChanged();
    }

    private void ApplyExecutionResult(CommandResult result)
    {
        _executionStatus = result.Status;
        _executionMessage = result.Message;
        _executionResultText = JsonSerializer.Serialize(
            new
            {
                commandId = result.CommandId,
                correlationId = result.CorrelationId,
                status = result.Status,
                code = result.Code,
                message = result.Message,
                startedAt = result.StartedAt,
                completedAt = result.CompletedAt,
                durationMilliseconds = result.Duration.TotalMilliseconds,
                undoAvailable = result.UndoAvailable,
                data = result.Data,
            },
            ResultJsonOptions);
        IsExecutionResultOpen = true;
        NotifyExecutionResultChanged();
    }

    private void ClearExecutionResult()
    {
        _executionStatus = null;
        _executionMessage = string.Empty;
        _executionResultText = string.Empty;
        IsExecutionResultOpen = false;
        NotifyExecutionResultChanged();
    }

    private void SetSuccessfulPreview(bool value)
    {
        if (_hasSuccessfulPreview == value)
        {
            return;
        }

        _hasSuccessfulPreview = value;
        if (!value && _isReviewApproved)
        {
            _isReviewApproved = false;
            OnPropertyChanged(nameof(IsReviewApproved));
        }
        NotifyAvailabilityChanged();
    }

    private void ResetReviewState()
    {
        _reviewVersion++;
        _lastApprovedPlan = null;
        _hasSuccessfulPreview = false;
        if (_isReviewApproved)
        {
            _isReviewApproved = false;
            OnPropertyChanged(nameof(IsReviewApproved));
        }
        NotifyAvailabilityChanged();
    }

    private void NotifyAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanRunSelectedTool));
        OnPropertyChanged(nameof(PrimaryActionLabel));
        OnPropertyChanged(nameof(IsMutatingTool));
        OnPropertyChanged(nameof(CanApproveReview));
        ExecuteSelectedToolCommand.NotifyCanExecuteChanged();
    }

    private void NotifyExecutionResultChanged()
    {
        OnPropertyChanged(nameof(HasExecutionResult));
        OnPropertyChanged(nameof(IsExecutionSuccess));
        OnPropertyChanged(nameof(IsExecutionError));
        OnPropertyChanged(nameof(IsExecutionCancelled));
        OnPropertyChanged(nameof(ExecutionTitle));
        OnPropertyChanged(nameof(ExecutionMessage));
        OnPropertyChanged(nameof(ExecutionResultText));
    }
}
