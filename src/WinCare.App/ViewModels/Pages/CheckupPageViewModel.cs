using CommunityToolkit.Mvvm.Input;
using WinCare.Application.Commands;
using WinCare.App.Services;
using WinCare.Domain.Commands;

namespace WinCare.App.ViewModels.Pages;

public sealed class CheckupPageViewModel : TabbedPageViewModel
{
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

    public CheckupPageViewModel() : this(AppRuntime.Current.Dispatcher) { }

    internal CheckupPageViewModel(CommandDispatcher dispatcher)
        : base([
            new PageSection("Quick check", "No quick check results are available.", [
                new PageRow("Windows and hardware", "Build, uptime, memory, processor, and device basics.", "Ready", "Read-only"),
                new PageRow("Storage", "Free space, volume state, and pressure thresholds.", "Ready", "Read-only"),
                new PageRow("Security", "Windows Security, firewall, updates, and restart state.", "Ready", "Read-only"),
                new PageRow("Updates", "Search Windows Update readiness without installing anything.", "Ready", "Read-only")]),
            new PageSection("Full check", "A full check has not been run.", [
                new PageRow("Complete diagnostic set", "Runs every admitted read-only check with individual progress and deadlines.", "Ready", "Cancellable")]),
            new PageSection("Custom check", "Choose at least one area to build a custom check.", [
                new PageRow("Select areas", "Choose system, storage, security, applications, network, or advanced checks.", "Ready", "Read-only")]),
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

    private async Task RunQuickCheckAsync()
    {
        IsRunning = true;
        RunSummary = "Collecting read-only evidence…";
        HealthScoreText = "…";
        HealthScoreDetail = "checking";

        int succeeded = 0;
        foreach ((string commandId, string rowTitle) in QuickCheckCommands)
        {
            PageRow? row = CurrentRows.FirstOrDefault(candidate => candidate.Title == rowTitle);
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

        HealthScoreText = "—";
        HealthScoreDetail = succeeded == QuickCheckCommands.Length ? "evidence collected" : "review results";
        RunSummary = $"{succeeded} of {QuickCheckCommands.Length} read-only checks completed. Review category details before taking any action.";
        IsRunning = false;
    }
}
