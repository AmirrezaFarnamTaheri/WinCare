using CommunityToolkit.Mvvm.Input;
using WinCare.Application.Commands;
using WinCare.App.Services;
using WinCare.Domain.Commands;

namespace WinCare.App.ViewModels.Pages;

public sealed class CheckupPageViewModel : TabbedPageViewModel
{
    private const int ResultsSectionIndex = 1;
    private static readonly (string CommandId, string RowTitle)[] QuickCheckCommands =
    [
        ("system", "Windows and hardware"),
        ("storage", "Storage"),
        ("security", "Security"),
        ("wua-search", "Updates"),
    ];

    private readonly CommandDispatcher _dispatcher;
    private bool _isRunning;
    private string _runSummary = "No check has been run yet.";
    private string _healthScoreText = "—";
    private string _healthScoreDetail = "awaiting check";
    private bool _hasResults;

    public CheckupPageViewModel() : this(AppRuntime.Current.Dispatcher) { }

    internal CheckupPageViewModel(CommandDispatcher dispatcher)
        : base([
            new PageSection("Quick check", "No quick check results are available.", [
                new PageRow("Windows and hardware", "Build, uptime, memory, processor, and device basics.", "Ready", "Read-only"),
                new PageRow("Storage", "Free space, volume state, and pressure thresholds.", "Ready", "Read-only"),
                new PageRow("Security", "Windows Security, firewall, updates, and restart state.", "Ready", "Read-only"),
                new PageRow("Updates", "Search Windows Update readiness without installing anything.", "Ready", "Read-only")]),
            new PageSection("Results", "Completed check results will be listed here.", [])])
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        RunQuickCheckCommand = new AsyncRelayCommand(RunQuickCheckAsync, () => !IsRunning);
    }

    public IAsyncRelayCommand RunQuickCheckCommand { get; }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                RunQuickCheckCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(RunActionText));
            }
        }
    }

    public string RunActionText => IsRunning ? "Checking your PC…" : "Run a read-only check";

    public string RunSummary
    {
        get => _runSummary;
        private set => SetProperty(ref _runSummary, value);
    }

    public string HealthScoreText
    {
        get => _healthScoreText;
        private set => SetProperty(ref _healthScoreText, value);
    }

    public string HealthScoreDetail
    {
        get => _healthScoreDetail;
        private set => SetProperty(ref _healthScoreDetail, value);
    }

    public override void SelectSection(int index)
    {
        base.SelectSection(index);
        if (index != ResultsSectionIndex)
        {
            return;
        }

        CurrentRows.Clear();
        if (_hasResults)
        {
            foreach (PageRow row in Sections[0].Rows)
            {
                row.IsCompact = IsCompactLayout;
                CurrentRows.Add(row);
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private async Task RunQuickCheckAsync()
    {
        IsRunning = true;
        RunSummary = "Collecting read-only evidence…";
        HealthScoreText = "…";
        HealthScoreDetail = "checking";

        int succeeded = 0;
        try
        {
            foreach ((string commandId, string rowTitle) in QuickCheckCommands)
            {
                PageRow? row = Sections[0].Rows.FirstOrDefault(candidate => candidate.Title == rowTitle);
                if (row is not null)
                {
                    row.State = "Checking";
                    row.Detail = "Read-only";
                }

                CommandResult result;
                try
                {
                    result = await _dispatcher.ExecuteAsync(
                        CommandRequest.Preview(commandId),
                        new CommandExecutionOptions(false, DateTimeOffset.UtcNow.AddMinutes(2)),
                        CancellationToken.None);
                }
                catch (Exception)
                {
                    result = new CommandResult(commandId, Guid.NewGuid(), CommandResultStatus.Failed,
                        "checkup.dispatch_exception", "WinCare could not complete this read-only check.", null,
                        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false);
                }

                if (result.Status == CommandResultStatus.Succeeded)
                {
                    succeeded++;
                }

                if (row is not null)
                {
                    row.State = result.Status == CommandResultStatus.Succeeded ? "Checked" : "Needs review";
                    row.Detail = result.Message;
                }
            }

            _hasResults = true;
            HealthScoreText = "—";
            HealthScoreDetail = succeeded == QuickCheckCommands.Length ? "evidence collected" : "review results";
            RunSummary = $"{succeeded} of {QuickCheckCommands.Length} read-only checks completed. Review category details before taking any action.";

            if (SelectedIndex == ResultsSectionIndex)
            {
                SelectSection(0);
                SelectSection(ResultsSectionIndex);
            }
        }
        finally
        {
            IsRunning = false;
        }
    }
}
