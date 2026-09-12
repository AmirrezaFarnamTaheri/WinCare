using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private string _packageInventoryText = string.Empty;
    public string PackageInventoryText => _packageInventoryText;
    public bool HasPackageInventory => _packageInventoryText.Length > 0;
    private bool _isReviewApproved;
    private string _parameterJson = "{}";
    private bool _useAdvancedParameterJson;
    private ApprovedMutationPlan? _lastApprovedPlan;
    private long _reviewVersion;

    private CancellationTokenSource? _activeCts;
    private bool _isCancellationRequested;

    public ToolExecutionViewModel(CommandDispatcher dispatcher, Action<string> recordRecent)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _recordRecent = recordRecent ?? throw new ArgumentNullException(nameof(recordRecent));
        ExecuteSelectedToolCommand = new AsyncRelayCommand(
            ExecuteSelectedToolAsync,
            CanExecuteSelectedTool);
        CancelSelectedToolCommand = new RelayCommand(CancelSelectedTool, () => IsExecuting && !IsCancellationRequested);
    }

    public IAsyncRelayCommand ExecuteSelectedToolCommand { get; }
    public IRelayCommand CancelSelectedToolCommand { get; }
    public ObservableCollection<ToolParameterFieldViewModel> ParameterFields { get; } = new();

    public bool IsCancellationRequested
    {
        get => _isCancellationRequested;
        private set
        {
            if (SetProperty(ref _isCancellationRequested, value))
            {
                OnPropertyChanged(nameof(CancelActionLabel));
                OnPropertyChanged(nameof(PrimaryActionLabel));
                CancelSelectedToolCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string CancelActionLabel => IsCancellationRequested ? "Stopping…" : "Cancel";

    private void CancelSelectedTool()
    {
        if (_activeCts is null || IsCancellationRequested) return;
        IsCancellationRequested = true;
        _activeCts.Cancel();
    }

    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            if (SetProperty(ref _isExecuting, value))
                NotifyAvailabilityChanged();
        }
    }

    public bool IsExecutionResultOpen
    {
        get => _isExecutionResultOpen;
        set => SetProperty(ref _isExecutionResultOpen, value);
    }

    public bool CanRunSelectedTool => !IsExecuting &&
        (_selectedTool?.Definition.MigrationStatus is MigrationStatus.Implemented or MigrationStatus.BehaviorVerified);

    public string PrimaryActionLabel
    {
        get
        {
            if (IsExecuting) return IsCancellationRequested ? "Stopping…" : "Running";
            if (IsSafeTool) return "Run tool";
            if (IsDestructiveTool) return IsReviewApproved ? "Apply destructive change" : "Preview impact";
            return IsReviewApproved ? "Apply changes" : "Review changes";
        }
    }

    public bool HasExecutionResult => _executionStatus is not null;
    public bool IsExecutionSuccess => _executionStatus == CommandResultStatus.Succeeded;
    public bool IsExecutionError => _executionStatus is CommandResultStatus.Blocked or CommandResultStatus.Failed or CommandResultStatus.NotMigrated;
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
    public bool IsPresetTool => _selectedTool?.Id == "preset";
    public string PresetContents
    {
        get
        {
            if (!IsPresetTool) return string.Empty;
            if (!TryBuildExecutionParameters(out JsonElement parameters, out _) ||
                !parameters.TryGetProperty("PresetId", out JsonElement id) || id.ValueKind != JsonValueKind.String)
                return "Choose a preset to see its included changes.";
            PresetDefinition? preset = WinCare.CommandCatalog.RemediationCatalog.LoadPresets()
                .FirstOrDefault(item => string.Equals(item.Id, id.GetString(), StringComparison.OrdinalIgnoreCase));
            if (preset is null) return "This preset is not in the built-in catalog. Choose a listed plan.";
            var rules = WinCare.CommandCatalog.RemediationCatalog.LoadRules().ToDictionary(rule => rule.Id);
            return preset.Title + "\n\n" + string.Join("\n\n", preset.RuleIds.Select(ruleId =>
            {
                RemediationRule rule = rules[ruleId];
                string builds = rule.Compatibility.MaxBuild is int maximum
                    ? $"Windows builds {rule.Compatibility.MinBuild}–{maximum}"
                    : $"Windows build {rule.Compatibility.MinBuild} or later";
                return $"{rule.Title}\n{rule.Description}\n{rule.Risk} risk · {builds}\nRecovery: {rule.Recovery}";
            })) + "\n\nCompatibility and current targets are checked during preview. Recovery guidance does not guarantee one-click Undo.";
        }
    }
    public string ActionFlowTitle => IsSafeTool
        ? (IsMutatingTool ? "Direct, low-risk change" : "Read-only inspection")
        : IsReviewApproved
            ? "Reviewed and ready"
            : "Preview before applying";

    public string ActionFlowDescription => IsSafeTool
        ? (IsMutatingTool
            ? "WinCare will run this bounded action and record the outcome in Activity."
            : "This tool gathers evidence without changing Windows. The result is recorded in Activity.")
        : IsReviewApproved
            ? "The approved preview matches the current inputs. Applying consumes this approval once and records the outcome."
            : "Run a preview to resolve targets and impact. Review the result, then approve this exact plan to apply it.";

    /// <summary>Raw JSON editor used only when Advanced parameter mode is enabled.</summary>
    public string ParameterJson
    {
        get => _parameterJson;
        set
        {
            if (SetProperty(ref _parameterJson, value ?? "{}"))
                ResetReviewState();
        }
    }

    /// <summary>
    /// Switches from generated typed controls to an explicit raw JSON escape hatch. Entering
    /// Advanced mode starts from the current structured values; returning imports known fields.
    /// </summary>
    public bool UseAdvancedParameterJson
    {
        get => _useAdvancedParameterJson;
        set
        {
            if (_useAdvancedParameterJson == value) return;

            if (value)
            {
                if (TryBuildStructuredParameters(out JsonElement structured, out _))
                    _parameterJson = JsonSerializer.Serialize(structured, new JsonSerializerOptions { WriteIndented = true });
            }
            else
            {
                if (!TryImportAdvancedValues()) return;
            }

            if (SetProperty(ref _useAdvancedParameterJson, value))
            {
                OnPropertyChanged(nameof(StructuredParameterEditorVisible));
                OnPropertyChanged(nameof(AdvancedParameterEditorVisible));
                ResetReviewState();
            }
        }
    }

    public bool StructuredParameterEditorVisible => !UseAdvancedParameterJson;
    public bool AdvancedParameterEditorVisible => UseAdvancedParameterJson;
    public bool HasStructuredParameters => ParameterFields.Count > 0;
    public string ParameterEditorSummary => HasStructuredParameters
        ? $"{ParameterFields.Count} declared input{(ParameterFields.Count == 1 ? string.Empty : "s")}. Required fields are marked with *."
        : "This command takes no declared inputs. Use Advanced JSON only for an explicitly documented extension field.";

    public bool IsSafeTool => _selectedTool is null || _selectedTool.Definition.RiskTier == RiskTier.Safe;
    public bool IsModerateTool => _selectedTool?.Definition.RiskTier == RiskTier.Moderate;
    public bool IsDestructiveTool => _selectedTool?.Definition.RiskTier == RiskTier.Destructive;
    public bool IsMutatingTool => _selectedTool?.Definition.ReadOnly == false;
    public bool RequiresApprovalSwitch => IsMutatingTool && !IsSafeTool;
    // F-004: approval is only possible after a successful preview, for every mutating tier,
    // so the receipt presented at execution always corresponds to reviewed resolved targets.
    public bool CanApproveReview => IsMutatingTool && !IsExecuting && _hasSuccessfulPreview;

    public bool IsReviewApproved
    {
        get => _isReviewApproved;
        set
        {
            bool next = value && CanApproveReview;
            if (SetProperty(ref _isReviewApproved, next))
            {
                OnPropertyChanged(nameof(PrimaryActionLabel));
                OnPropertyChanged(nameof(ActionFlowTitle));
                OnPropertyChanged(nameof(ActionFlowDescription));
            }
        }
    }

    public void SelectTool(ToolRowViewModel? tool)
    {
        if (ReferenceEquals(_selectedTool, tool)) return;

        _selectedTool = tool;
        ConfigureParameterFields(tool);
        ResetReviewState();
        ClearExecutionResult();
        NotifyAvailabilityChanged();
    }

    private void ConfigureParameterFields(ToolRowViewModel? tool)
    {
        foreach (ToolParameterFieldViewModel field in ParameterFields)
            field.PropertyChanged -= OnParameterFieldChanged;

        ParameterFields.Clear();
        if (tool is not null)
        {
            foreach (CommandParameterDefinition definition in CommandParameterCatalog.For(tool.Id))
            {
                var field = new ToolParameterFieldViewModel(definition);
                field.PropertyChanged += OnParameterFieldChanged;
                ParameterFields.Add(field);
            }
        }

        _useAdvancedParameterJson = false;
        _parameterJson = "{}";
        if (TryBuildStructuredParameters(out JsonElement structured, out _))
            _parameterJson = JsonSerializer.Serialize(structured);

        OnPropertyChanged(nameof(ParameterFields));
        OnPropertyChanged(nameof(HasStructuredParameters));
        OnPropertyChanged(nameof(ParameterEditorSummary));
        OnPropertyChanged(nameof(UseAdvancedParameterJson));
        OnPropertyChanged(nameof(StructuredParameterEditorVisible));
        OnPropertyChanged(nameof(AdvancedParameterEditorVisible));
        OnPropertyChanged(nameof(ParameterJson));
    }

    private void OnParameterFieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ToolParameterFieldViewModel.Value)) return;
        if (!UseAdvancedParameterJson && TryBuildStructuredParameters(out JsonElement structured, out _))
        {
            _parameterJson = JsonSerializer.Serialize(structured);
            OnPropertyChanged(nameof(ParameterJson));
        }
        ResetReviewState();
    }

    private bool CanExecuteSelectedTool() => CanRunSelectedTool;

    private async Task ExecuteSelectedToolAsync(CancellationToken cancellationToken)
    {
        ToolRowViewModel? selected = _selectedTool;
        if (selected is null || !CanRunSelectedTool) return;

        bool apply = IsMutatingTool && (IsSafeTool || IsReviewApproved);
        long reviewVersion = _reviewVersion;
        if (apply && !CanApproveReview && !IsSafeTool) return;

        if (!TryBuildExecutionParameters(out JsonElement parameters, out string parameterError))
        {
            SetParameterError(parameterError);
            return;
        }

        IsExecuting = true;
        ClearExecutionResult();
        try
        {
            // F-004: every non-Safe mutation must carry the single-use review plan issued by
            // the preceding preview, so applied changes always have reviewed, resolved targets.
            ApprovedMutationPlan? approval = (apply && !IsSafeTool) ? _lastApprovedPlan : null;
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
            IsCancellationRequested = false;
        }
    }

    private bool TryBuildExecutionParameters(out JsonElement parameters, out string error)
    {
        if (!UseAdvancedParameterJson)
            return TryBuildStructuredParameters(out parameters, out error);

        string parameterText = string.IsNullOrWhiteSpace(ParameterJson) ? "{}" : ParameterJson;
        if (parameterText.Length > MaxParameterJsonCharacters)
        {
            parameters = default;
            error = "Command parameter JSON exceeds the 1 MiB safety limit.";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(parameterText);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                parameters = default;
                error = "Command parameters must be a JSON object.";
                return false;
            }
            parameters = document.RootElement.Clone();
            error = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            parameters = default;
            error = "Advanced command parameters are not valid JSON.";
            return false;
        }
    }

    private bool TryBuildStructuredParameters(out JsonElement parameters, out string error)
    {
        var root = new JsonObject();
        foreach (ToolParameterFieldViewModel field in ParameterFields)
        {
            string raw = field.Value.Trim();
            if (raw.Length == 0)
            {
                if (field.Required)
                {
                    parameters = default;
                    error = $"{field.Label.TrimEnd(' ', '*')} is required.";
                    return false;
                }
                continue;
            }

            if (field.HasOptions && !field.Options.Contains(raw, StringComparer.OrdinalIgnoreCase))
            {
                parameters = default;
                error = $"{field.Name} must be one of: {string.Join(", ", field.Options)}.";
                return false;
            }

            try
            {
                switch (field.Kind)
                {
                    case CommandParameterKind.Text:
                        root[field.Name] = raw;
                        break;
                    case CommandParameterKind.DateTime:
                        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset dateTime))
                            throw new FormatException("must be an ISO-8601 date/time");
                        root[field.Name] = dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
                        break;
                    case CommandParameterKind.Boolean:
                        if (!bool.TryParse(raw, out bool boolean)) throw new FormatException("must be true or false");
                        root[field.Name] = boolean;
                        break;
                    case CommandParameterKind.Integer:
                        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer))
                            throw new FormatException("must be an integer");
                        ValidateRange(field, integer);
                        root[field.Name] = integer;
                        break;
                    case CommandParameterKind.Long:
                        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longInteger))
                            throw new FormatException("must be an integer");
                        ValidateRange(field, longInteger);
                        root[field.Name] = longInteger;
                        break;
                    case CommandParameterKind.Number:
                        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
                            throw new FormatException("must be a finite number");
                        ValidateRange(field, number);
                        root[field.Name] = number;
                        break;
                    case CommandParameterKind.StringList:
                    {
                        string[] values = raw.Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Where(value => value.Length > 0)
                            .Distinct(StringComparer.Ordinal)
                            .ToArray();
                        if (field.Required && values.Length == 0) throw new FormatException("requires at least one value");
                        root[field.Name] = new JsonArray(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
                        break;
                    }
                    case CommandParameterKind.Json:
                        root[field.Name] = JsonNode.Parse(raw) ?? throw new FormatException("must contain a JSON value");
                        break;
                    default:
                        throw new FormatException("uses an unsupported parameter type");
                }
            }
            catch (Exception ex) when (ex is FormatException or JsonException or OverflowException)
            {
                parameters = default;
                error = $"{field.Name} {ex.Message}.";
                return false;
            }
        }

        using JsonDocument document = JsonDocument.Parse(root.ToJsonString());
        parameters = document.RootElement.Clone();
        error = string.Empty;
        return true;
    }

    private static void ValidateRange(ToolParameterFieldViewModel field, double value)
    {
        if (field.Definition.Minimum is string minimum &&
            double.TryParse(minimum, NumberStyles.Float, CultureInfo.InvariantCulture, out double min) && value < min)
            throw new FormatException($"must be at least {minimum}");
        if (field.Definition.Maximum is string maximum &&
            double.TryParse(maximum, NumberStyles.Float, CultureInfo.InvariantCulture, out double max) && value > max)
            throw new FormatException($"must be at most {maximum}");
    }

    private bool TryImportAdvancedValues()
    {
        if (_parameterJson.Length > MaxParameterJsonCharacters)
        {
            SetParameterError("Command parameter JSON exceeds the 1 MiB safety limit.");
            return false;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(string.IsNullOrWhiteSpace(_parameterJson) ? "{}" : _parameterJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                SetParameterError("Command parameters must be a JSON object. Fix the JSON before switching editors.");
                return false;
            }
            foreach (ToolParameterFieldViewModel field in ParameterFields)
            {
                if (!document.RootElement.TryGetProperty(field.Name, out JsonElement value)) continue;
                field.Value = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString() ?? string.Empty,
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Array when field.Kind == CommandParameterKind.StringList =>
                        string.Join(Environment.NewLine, value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString())),
                    _ => value.GetRawText(),
                };
            }
            return true;
        }
        catch (JsonException ex)
        {
            // F-007: invalid advanced JSON is surfaced immediately instead of silently
            // ignored; the raw editor keeps the text and execution stays blocked.
            SetParameterError($"Advanced parameter JSON is invalid: {ex.Message} Fix the JSON before running the command.");
            return false;
        }
    }

    private static TimeSpan ExecutionBudget(CommandDefinition definition) => definition.Id switch
    {
        "download-start" or "download-batch" or "download-start-due" or "steam-backup" or "steam-restore" => TimeSpan.FromHours(2),
        "wua-download" or "wua-install" or "wua-uninstall" or
        "offline-appx-selection" or "offline-driver-add" or "offline-driver-remove" or
        "offline-package-add" or "offline-feature-set" or "offline-reduction-apply" or
        "provisioning-plan" or "appx-selection" or "hardening-apply" => TimeSpan.FromHours(1),
        _ when !definition.ReadOnly => TimeSpan.FromMinutes(15),
        _ => TimeSpan.FromMinutes(2),
    };

    private void SetParameterError(string message)
    {
        _packageInventoryText = string.Empty;
        _executionStatus = CommandResultStatus.Blocked;
        _executionMessage = message;
        _executionResultText = JsonSerializer.Serialize(new
        {
            status = CommandResultStatus.Blocked,
            code = "command.parameters_invalid",
            message,
        }, ResultJsonOptions);
        IsExecutionResultOpen = true;
        NotifyExecutionResultChanged();
    }

    private void ApplyExecutionResult(CommandResult result)
    {
        _packageInventoryText = PackageInventoryPresentation.Format(result.CommandId, result.Data);
        _executionStatus = result.Status;
        _executionMessage = result.Message;
        try
        {
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
        }
        catch (Exception)
        {
            _executionResultText = result.Message;
        }
        IsExecutionResultOpen = !HasPackageInventory;
        NotifyExecutionResultChanged();
    }

    private void ClearExecutionResult()
    {
        _packageInventoryText = string.Empty;
        _executionStatus = null;
        _executionMessage = string.Empty;
        _executionResultText = string.Empty;
        IsExecutionResultOpen = false;
        NotifyExecutionResultChanged();
    }

    private void SetSuccessfulPreview(bool value)
    {
        if (_hasSuccessfulPreview == value) return;
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
        OnPropertyChanged(nameof(IsSafeTool));
        OnPropertyChanged(nameof(IsModerateTool));
        OnPropertyChanged(nameof(IsDestructiveTool));
        OnPropertyChanged(nameof(RequiresApprovalSwitch));
        OnPropertyChanged(nameof(CanApproveReview));
        OnPropertyChanged(nameof(ActionFlowTitle));
        OnPropertyChanged(nameof(ActionFlowDescription));
        ExecuteSelectedToolCommand.NotifyCanExecuteChanged();
        CancelSelectedToolCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsPresetTool));
        OnPropertyChanged(nameof(PresetContents));
    }

    private void NotifyExecutionResultChanged()
    {
        OnPropertyChanged(nameof(PackageInventoryText));
        OnPropertyChanged(nameof(HasPackageInventory));
        OnPropertyChanged(nameof(HasExecutionResult));
        OnPropertyChanged(nameof(IsExecutionSuccess));
        OnPropertyChanged(nameof(IsExecutionError));
        OnPropertyChanged(nameof(IsExecutionCancelled));
        OnPropertyChanged(nameof(ExecutionTitle));
        OnPropertyChanged(nameof(ExecutionMessage));
        OnPropertyChanged(nameof(ExecutionResultText));
    }
}
