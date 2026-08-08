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
    private CommandResultStatus? _executionStatus;
    private string _executionMessage = string.Empty;
    private string _executionResultText = string.Empty;

    public ToolExecutionViewModel(CommandDispatcher dispatcher, Action<string> recordRecent)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _recordRecent = recordRecent ?? throw new ArgumentNullException(nameof(recordRecent));
        ExecuteSelectedToolCommand = new AsyncRelayCommand(
            ExecuteSelectedToolAsync,
            CanExecuteSelectedTool);
    }

    public IAsyncRelayCommand ExecuteSelectedToolCommand { get; }

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
        : _selectedTool?.Definition.ReadOnly == false
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

    public void SelectTool(ToolRowViewModel? tool)
    {
        if (ReferenceEquals(_selectedTool, tool))
        {
            return;
        }

        _selectedTool = tool;
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

        IsExecuting = true;
        ClearExecutionResult();
        try
        {
            JsonElement parameters = JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
            CommandRequest request = CommandRequest.Preview(selected.Id, parameters);
            CommandResult result = await _dispatcher.ExecuteAsync(
                request,
                new CommandExecutionOptions(
                    ReviewApproved: false,
                    Deadline: DateTimeOffset.UtcNow.AddSeconds(30)),
                cancellationToken);

            _recordRecent(result.CommandId);
            if (string.Equals(_selectedTool?.Id, result.CommandId, StringComparison.Ordinal))
            {
                ApplyExecutionResult(result);
            }
        }
        finally
        {
            IsExecuting = false;
        }
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

    private void NotifyAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanRunSelectedTool));
        OnPropertyChanged(nameof(PrimaryActionLabel));
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
