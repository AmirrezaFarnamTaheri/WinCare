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
    private ApprovedMutationPlan? _pendingCleanPlan;
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

            IReadOnlyList<CommandResult> fastResults = await ParallelCommandProbeRunner.RunPreviewsAsync(
                _dispatcher,
                FastCheckCommands.Select(item => item.CommandId).ToArray(),
                TimeSpan.FromSeconds(3),
                maxConcurrency: 3,
                cancellationToken: CancellationToken.None);

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
            }

            // F-015: findings are represented once on the quick-check rows; the Results tab
            // is derived from that same evaluated state afterwards, so the two views can
            // never disagree about a check's classification.
            _hasResults = true;
            EvaluateFindings(fastDict, null);
            RebuildResultRowsFromQuickChecks();

            if (SelectedIndex == ResultsSectionIndex)
            {
                SelectSection(0);
                SelectSection(ResultsSectionIndex);
            }

            // F-014: one owned run — the check stays active until the background Windows
            // Update search settles (its dispatcher deadline bounds the wait), so a second
            // check cannot start while COM work is still in flight.
            Task wuaCompletion = wuaTask.ContinueWith(t =>
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

            await wuaCompletion;
        }
        finally
        {
            if (runVersion == _runVersion)
            {
                IsRunning = false;
            }
        }
    }

    /// <summary>
    /// F-015: rebuilds the Results rows from the evaluated quick-check rows, so the Results
    /// tab always mirrors the final classification instead of a pre-evaluation copy.
    /// </summary>
    private void RebuildResultRowsFromQuickChecks()
    {
        _resultRows.Clear();
        foreach (PageRow quickRow in Sections[0].Rows)
        {
            string description = FastCheckCommands.FirstOrDefault(item => item.RowTitle == quickRow.Title).CommandId
                ?? (quickRow.Title == WuaRowTitle ? WuaCommandId : quickRow.Description);
            _resultRows.Add(new PageRow(quickRow.Title, description, quickRow.State, quickRow.Detail)
            {
                StatusBrushKey = quickRow.StatusBrushKey,
                ActionText = quickRow.ActionText,
                ActionCommand = quickRow.ActionCommand,
            });
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
                        if (freeGb < WinCare.Domain.Assessment.AssessmentPolicy.DiskFreeCriticalGb)
                        {
                            hasCritical = true;
                            findings.Add($"Low space on {driveName} ({freeGb:0.0} GB free)");
                            if (storageRow is not null)
                            {
                                storageRow.State = "Critical space";
                                storageRow.StatusBrushKey = "DangerBrush";
                                storageRow.ActionText = "Clean Temp";
                                storageRow.ActionCommand = new AsyncRelayCommand(RunQuickCleanAsync);
                            }
                        }
                        else if (freeGb < WinCare.Domain.Assessment.AssessmentPolicy.DiskFreeWarningGb)
                        {
                            hasWarning = true;
                            findings.Add($"Moderate space on {driveName} ({freeGb:0.0} GB free)");
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
        // F-004: cleaner-disk-pressure is a Moderate mutation, so the quick clean follows the
        // single admission contract: preview (review resolved targets, obtain the single-use
        // receipt) first, then apply with explicit approval on the confirming click.
        PageRow? storageRow = Sections[0].Rows.FirstOrDefault(candidate => candidate.Title == "Storage");
        try
        {
            if (_pendingCleanPlan is not { } plan)
            {
                CommandResult preview = await _dispatcher.ExecuteAsync(
                    CommandRequest.Preview("cleaner-disk-pressure", JsonSerializer.SerializeToElement(new { })),
                    CommandExecutionOptions.Default,
                    CancellationToken.None);

                if (preview.Status == CommandResultStatus.Succeeded && preview.ReviewPlan is not null)
                {
                    _pendingCleanPlan = preview.ReviewPlan;
                    if (storageRow is not null)
                    {
                        storageRow.State = "Review cleanup";
                        storageRow.Detail = string.IsNullOrWhiteSpace(preview.Message)
                            ? "Review the resolved cleanup targets, then confirm to apply."
                            : preview.Message;
                        storageRow.StatusBrushKey = "AccentTealBrush";
                        storageRow.ActionText = "Confirm Clean";
                    }
                    return;
                }

                if (storageRow is not null)
                {
                    storageRow.State = "Cleanup failed";
                    storageRow.Detail = preview.Message;
                    storageRow.StatusBrushKey = "WarningBrush";
                }
                return;
            }

            CommandResult result = await _dispatcher.ExecuteAsync(
                CommandRequest.Execute("cleaner-disk-pressure", JsonSerializer.SerializeToElement(new { }), plan),
                new CommandExecutionOptions(ReviewApproved: true, Deadline: DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2)),
                CancellationToken.None);
            _pendingCleanPlan = null; // single-use receipt consumed

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
            _dispatcherQueue.TryEnqueue(() => action());
        }
        else
        {
            action();
        }
    }
}
