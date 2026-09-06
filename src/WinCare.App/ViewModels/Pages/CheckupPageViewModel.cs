using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using WinCare.Application.Commands;
using WinCare.App.Services;
using WinCare.Domain.Commands;

namespace WinCare.App.ViewModels.Pages;

public sealed class CheckupPageViewModel : TabbedPageViewModel
{
    private const int ResultsSectionIndex = 1;

    private static readonly (string CommandId, string RowTitle)[] FastCheckCommands =
    [
        ("system", "Windows and hardware"),
        ("storage", "Storage"),
        ("security", "Security"),
    ];

    private const string WuaCommandId = "wua-search";
    private const string WuaRowTitle = "Updates";

    private readonly CommandDispatcher _dispatcher;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    private readonly List<PageRow> _resultRows = [];
    private bool _isRunning;
    private string _runSummary = "No check has been run yet.";
    private string _healthScoreText = "—";
    private string _healthScoreDetail = "awaiting check";
    private string _healthScoreBrushKey = "AccentTealBrush";
    private bool _hasResults;
    private int _runVersion;

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

    public string HealthScoreBrushKey
    {
        get => _healthScoreBrushKey;
        private set => SetProperty(ref _healthScoreBrushKey, value);
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
            foreach (PageRow row in _resultRows)
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
        int runVersion = ++_runVersion;
        IsRunning = true;
        RunSummary = "Collecting read-only evidence concurrently…";
        HealthScoreText = "…";
        HealthScoreDetail = "checking";
        HealthScoreBrushKey = "AccentTealBrush";

        try
        {
            foreach ((_, string rowTitle) in FastCheckCommands)
            {
                PageRow? row = Sections[0].Rows.FirstOrDefault(candidate => candidate.Title == rowTitle);
                if (row is not null)
                {
                    row.State = "Checking";
                    row.Detail = "Read-only";
                    row.StatusBrushKey = "AccentTealBrush";
                    row.ActionText = null;
                    row.ActionCommand = null;
                }
            }

            PageRow? wuaRow = Sections[0].Rows.FirstOrDefault(candidate => candidate.Title == WuaRowTitle);
            if (wuaRow is not null)
            {
                wuaRow.State = "Checking in background…";
                wuaRow.Detail = "Searching Windows Update readiness in background…";
                wuaRow.StatusBrushKey = "AccentTealBrush";
                wuaRow.ActionText = null;
                wuaRow.ActionCommand = null;
            }

            // Keep the slower WUA COM query off the UI thread while fast probes finish first.
            Task<CommandResult> wuaTask = Task.Run(async () =>
            {
                try
                {
                    return await _dispatcher.ExecuteAsync(
                        CommandRequest.Preview(WuaCommandId),
                        new CommandExecutionOptions(ReviewApproved: false, Deadline: DateTimeOffset.UtcNow + TimeSpan.FromSeconds(25)),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return new CommandResult(
                        WuaCommandId,
                        Guid.NewGuid(),
                        CommandResultStatus.Failed,
                        "wua.query_error",
                        $"Background update query failed: {ex.Message}",
                        null,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        false);
                }
            });

            // Await on the captured UI context because the continuation updates bound rows.
            IReadOnlyList<CommandResult> fastResults = await ParallelCommandProbeRunner.RunPreviewsAsync(
                _dispatcher,
                FastCheckCommands.Select(item => item.CommandId).ToArray(),
                TimeSpan.FromSeconds(3),
                maxConcurrency: 3,
                cancellationToken: CancellationToken.None);

            _resultRows.Clear();
            var fastDict = new Dictionary<string, CommandResult>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < FastCheckCommands.Length; index++)
            {
                (string commandId, string rowTitle) = FastCheckCommands[index];
                CommandResult result = fastResults[index];
                fastDict[commandId] = result;

                PageRow? row = Sections[0].Rows.FirstOrDefault(candidate => candidate.Title == rowTitle);
                if (row is not null)
                {
                    row.State = result.Status == CommandResultStatus.Succeeded ? "Checked" : "Needs review";
                    row.Detail = result.Message;
                    row.StatusBrushKey = result.Status == CommandResultStatus.Succeeded ? "SuccessBrush" : "WarningBrush";
                }

                _resultRows.Add(new PageRow(
                    rowTitle,
                    commandId,
                    result.Status == CommandResultStatus.Succeeded ? "Collected" : "Needs review",
                    result.Message)
                {
                    StatusBrushKey = result.Status == CommandResultStatus.Succeeded ? "SuccessBrush" : "WarningBrush"
                });
            }

            _resultRows.Add(new PageRow(
                WuaRowTitle,
                WuaCommandId,
                "Checking in background…",
                "Searching Windows Update readiness in background…")
            {
                StatusBrushKey = "AccentTealBrush"
            });

            _hasResults = true;
            EvaluateFindings(fastDict, null);

            if (SelectedIndex == ResultsSectionIndex)
            {
                SelectSection(0);
                SelectSection(ResultsSectionIndex);
            }

            _ = wuaTask.ContinueWith(t =>
            {
                CommandResult wuaResult = (t.IsFaulted || t.IsCanceled)
                    ? new CommandResult(
                        WuaCommandId,
                        Guid.NewGuid(),
                        CommandResultStatus.Failed,
                        "wua.background_fault",
                        "Windows Update query was interrupted.",
                        null,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        false)
                    : t.Result;

                DispatchToUi(() =>
                {
                    if (runVersion == _runVersion)
                    {
                        ApplyWuaResult(wuaResult, fastDict);
                    }
                });
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        finally
        {
            if (runVersion == _runVersion)
            {
                IsRunning = false;
            }
        }
    }

    private void ApplyWuaResult(CommandResult wuaResult, Dictionary<string, CommandResult> fastDict)
    {
        PageRow? row = Sections[0].Rows.FirstOrDefault(candidate => candidate.Title == WuaRowTitle);
        PageRow? resRow = _resultRows.FirstOrDefault(candidate => candidate.Title == WuaRowTitle);

        string state = wuaResult.Status == CommandResultStatus.Succeeded ? "Checked" : "Needs review";
        string brushKey = wuaResult.Status == CommandResultStatus.Succeeded ? "SuccessBrush" : "WarningBrush";
        string detail = wuaResult.Message;

        if (wuaResult.Status == CommandResultStatus.Succeeded && wuaResult.Data?.ValueKind == JsonValueKind.Array)
        {
            int count = wuaResult.Data.Value.GetArrayLength();
            if (count > 0)
            {
                state = $"{count} updates found";
                brushKey = "WarningBrush";
            }
            else
            {
                state = "Up to date";
                brushKey = "SuccessBrush";
            }
        }

        if (row is not null)
        {
            row.State = state;
            row.Detail = detail;
            row.StatusBrushKey = brushKey;
            row.ActionText = brushKey == "WarningBrush" ? "Windows Update" : null;
            row.ActionCommand = brushKey == "WarningBrush"
                ? new RelayCommand(() => LaunchProtocol("ms-settings:windowsupdate"))
                : null;
        }

        if (resRow is not null)
        {
            resRow.State = state;
            resRow.Detail = detail;
            resRow.StatusBrushKey = brushKey;
        }

        EvaluateFindings(fastDict, wuaResult);

        if (SelectedIndex == ResultsSectionIndex)
        {
            SelectSection(0);
            SelectSection(ResultsSectionIndex);
        }
    }

    private void EvaluateFindings(Dictionary<string, CommandResult> fastDict, CommandResult? wuaResult)
    {
        bool hasCritical = false;
        bool hasWarning = false;
        bool updatesPending = wuaResult is null;
        bool hasIncompleteProbe = fastDict.Values.Any(result => result.Status != CommandResultStatus.Succeeded) ||
            (wuaResult is not null && wuaResult.Status != CommandResultStatus.Succeeded);
        var findings = new List<string>();

        foreach ((string commandId, string rowTitle) in FastCheckCommands)
        {
            if (fastDict.TryGetValue(commandId, out CommandResult? result) && result.Status != CommandResultStatus.Succeeded)
            {
                findings.Add($"{rowTitle} check did not complete");
            }
        }
        if (wuaResult is not null && wuaResult.Status != CommandResultStatus.Succeeded)
        {
            findings.Add("Windows Update check did not complete");
        }

        if (fastDict.TryGetValue("storage", out CommandResult? storageResult) && storageResult.Status == CommandResultStatus.Succeeded)
        {
            PageRow? storageRow = Sections[0].Rows.FirstOrDefault(c => c.Title == "Storage");
            if (storageResult.Data?.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement drive in storageResult.Data.Value.EnumerateArray())
                {
                    if (drive.TryGetProperty("ready", out JsonElement ready) && ready.GetBoolean() &&
                        drive.TryGetProperty("freeBytes", out JsonElement freeElem))
                    {
                        long freeBytes = freeElem.GetInt64();
                        double freeGb = freeBytes / (1024.0 * 1024.0 * 1024.0);
                        string driveName = drive.TryGetProperty("name", out JsonElement nameElem) ? nameElem.GetString() ?? "Drive" : "Drive";
                        if (freeGb < 10.0)
                        {
                            hasCritical = true;
                            findings.Add($"Low space on {driveName} ({freeGb:0.1} GB free)");
                            if (storageRow is not null)
                            {
                                storageRow.State = "Critical space";
                                storageRow.StatusBrushKey = "DangerBrush";
                                storageRow.ActionText = "Clean Temp";
                                storageRow.ActionCommand = new AsyncRelayCommand(RunQuickCleanAsync);
                            }
                        }
                        else if (freeGb < 20.0)
                        {
                            hasWarning = true;
                            findings.Add($"Moderate space on {driveName} ({freeGb:0.1} GB free)");
                            if (storageRow is not null && storageRow.StatusBrushKey != "DangerBrush")
                            {
                                storageRow.State = "Space attention";
                                storageRow.StatusBrushKey = "WarningBrush";
                                storageRow.ActionText = "Clean Temp";
                                storageRow.ActionCommand = new AsyncRelayCommand(RunQuickCleanAsync);
                            }
                        }
                    }
                }
            }
        }

        if (fastDict.TryGetValue("security", out CommandResult? secResult) && secResult.Status == CommandResultStatus.Succeeded)
        {
            PageRow? secRow = Sections[0].Rows.FirstOrDefault(c => c.Title == "Security");
            if (secResult.Data?.ValueKind == JsonValueKind.Object)
            {
                if (secResult.Data.Value.TryGetProperty("defenderServiceRunning", out JsonElement defElem) && !defElem.GetBoolean())
                {
                    hasCritical = true;
                    findings.Add("Windows Defender not running");
                    if (secRow is not null)
                    {
                        secRow.State = "Defender stopped";
                        secRow.StatusBrushKey = "DangerBrush";
                        secRow.ActionText = "Windows Security";
                        secRow.ActionCommand = new RelayCommand(() => LaunchProtocol("windowsdefender:"));
                    }
                }
                if (secResult.Data.Value.TryGetProperty("firewallEnabled", out JsonElement fwElem) && !fwElem.GetBoolean())
                {
                    hasCritical = true;
                    findings.Add("Firewall disabled");
                    if (secRow is not null)
                    {
                        secRow.State = "Firewall disabled";
                        secRow.StatusBrushKey = "DangerBrush";
                    }
                }
            }
        }

        if (wuaResult is not null && wuaResult.Status == CommandResultStatus.Succeeded &&
            wuaResult.Data?.ValueKind == JsonValueKind.Array)
        {
            int count = wuaResult.Data.Value.GetArrayLength();
            if (count > 0)
            {
                hasWarning = true;
                findings.Add($"{count} updates waiting");
            }
        }

        if (hasCritical)
        {
            HealthScoreText = "Action";
            HealthScoreDetail = "issues require action";
            HealthScoreBrushKey = "DangerBrush";
            RunSummary = $"Action recommended: {string.Join("; ", findings)}. Review category details below.";
        }
        else if (hasWarning)
        {
            HealthScoreText = "Attention";
            HealthScoreDetail = "items need attention";
            HealthScoreBrushKey = "WarningBrush";
            RunSummary = $"Needs attention: {string.Join("; ", findings)}. Review category details below.";
        }
        else if (hasIncompleteProbe)
        {
            HealthScoreText = "Review";
            HealthScoreDetail = "some checks incomplete";
            HealthScoreBrushKey = "WarningBrush";
            RunSummary = $"Some diagnostics did not complete: {string.Join("; ", findings)}.";
        }
        else if (updatesPending)
        {
            HealthScoreText = "Checking";
            HealthScoreDetail = "Windows Update still checking";
            HealthScoreBrushKey = "AccentTealBrush";
            RunSummary = "Fast diagnostics completed. Windows Update readiness is still being checked in the background.";
        }
        else
        {
            HealthScoreText = "Healthy";
            HealthScoreDetail = "checked areas look healthy";
            HealthScoreBrushKey = "SuccessBrush";
            RunSummary = "Completed diagnostics found no critical warnings, update backlog, or disk pressure.";
        }
    }

    private async Task RunQuickCleanAsync()
    {
        PageRow? storageRow = Sections[0].Rows.FirstOrDefault(candidate => candidate.Title == "Storage");
        try
        {
            CommandResult result = await _dispatcher.ExecuteAsync(
                CommandRequest.Execute("cleaner-disk-pressure", JsonSerializer.SerializeToElement(new { })),
                new CommandExecutionOptions(ReviewApproved: false, Deadline: DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2)),
                CancellationToken.None);

            if (result.Status != CommandResultStatus.Succeeded)
            {
                if (storageRow is not null)
                {
                    storageRow.State = "Cleanup failed";
                    storageRow.Detail = result.Message;
                    storageRow.StatusBrushKey = "WarningBrush";
                }
                return;
            }

            await RunQuickCheckAsync();
        }
        catch (Exception ex)
        {
            if (storageRow is not null)
            {
                storageRow.State = "Cleanup failed";
                storageRow.Detail = ex.Message;
                storageRow.StatusBrushKey = "WarningBrush";
            }
        }
    }

    private static void LaunchProtocol(string uriString)
    {
        try
        {
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(uriString));
        }
        catch
        {
            // Protocol launch fallback
        }
    }

    private void DispatchToUi(Action action)
    {
        if (_dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(action);
        }
        else
        {
            action();
        }
    }
}
