using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using WinCare.Application.Commands;
using WinCare.Application.Navigation;
using WinCare.App.Services;
using WinCare.Domain.Commands;

namespace WinCare.App.ViewModels.Pages;

public sealed class CheckupPageViewModel : TabbedPageViewModel
{
    private const int ResultsSectionIndex = 1;

    // The "Review …" actions deep-link into these routes; resolve them from the routing table
    // once so a catalog rename fails at startup instead of silently breaking the action.
    private static readonly string SystemCareRoute = NavigationCatalog.Items.Single(item => item.Id == "system-care").Id;
    private static readonly string SecurityRoute = NavigationCatalog.Items.Single(item => item.Id == "security").Id;

    private static readonly (string CommandId, string RowTitle)[] FastCheckCommands =
    [
        ("system", "Windows and hardware"),
        ("storage", "Storage"),
        ("security", "Security"),
    ];

    private const string WuaCommandId = "wua-search";
    private const string WuaRowTitle = "Updates";

    private readonly CommandDispatcher _dispatcher;
    private readonly List<PageRow> _resultRows = [];
    private CancellationTokenSource? _runCts;
    private bool _isRunning;
    private bool _isStopping;
    private string _runSummary = "Run Checkup to see how things look.";
    private string _healthScoreText = "Not checked";
    private string _healthScoreDetail = "Run Checkup to see the latest results";
    private string _healthScoreBrushKey = "AccentTealBrush";

    public CheckupPageViewModel() : this(AppRuntime.Current.Dispatcher) { }

    internal CheckupPageViewModel(CommandDispatcher dispatcher)
        : base([
            new PageSection("Quick check", "Run Checkup to see the latest results.", [
                new PageRow("Windows and hardware", "Windows version, uptime, memory, processor, and device basics.", "Ready", "Read-only"),
                new PageRow("Storage", "Free space and drive status.", "Ready", "Read-only"),
                new PageRow("Security", "Windows Security, firewall, updates, and restart state.", "Ready", "Read-only"),
                new PageRow("Updates", "Check Windows Update without installing anything.", "Ready", "Read-only")]),
            new PageSection("Results", "Run Checkup to see results here.", [])])
    {
        _dispatcher = dispatcher;
        RunQuickCheckCommand = new AsyncRelayCommand(RunQuickCheckAsync, () => !IsRunning);
        StopCheckCommand = new RelayCommand(CancelRunningCheck, () => IsRunning && !IsStopping);
    }

    public IAsyncRelayCommand RunQuickCheckCommand { get; }
    public IRelayCommand StopCheckCommand { get; }

    /// <summary>
    /// Cancels an in-flight checkup. Used when the user navigates away from the cached page so
    /// late-arriving probe results cannot mutate rows the user is no longer looking at.
    /// </summary>
    public void CancelRunningCheck()
    {
        if (_runCts is null || !IsRunning || IsStopping) return;
        IsStopping = true;
        RunSummary = "Stopping the checkup. Waiting for the active checks to finish.";
        HealthScoreText = "Stopping";
        HealthScoreDetail = "waiting for active checks";
        _runCts.Cancel();
    }

    public bool IsStopping
    {
        get => _isStopping;
        private set
        {
            if (SetProperty(ref _isStopping, value))
            {
                StopCheckCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(RunActionText));
            }
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                RunQuickCheckCommand.NotifyCanExecuteChanged();
                StopCheckCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(RunActionText));
            }
        }
    }

    public string RunActionText => IsStopping ? "Stopping…" : IsRunning ? "Checking your PC…" : "Run checkup";
    public string RunSummary { get => _runSummary; private set => SetProperty(ref _runSummary, value); }
    public string HealthScoreText { get => _healthScoreText; private set => SetProperty(ref _healthScoreText, value); }
    public string HealthScoreDetail { get => _healthScoreDetail; private set => SetProperty(ref _healthScoreDetail, value); }
    public string HealthScoreBrushKey { get => _healthScoreBrushKey; private set => SetProperty(ref _healthScoreBrushKey, value); }

    public override void SelectSection(int index)
    {
        base.SelectSection(index);
        if (index == ResultsSectionIndex) ShowResultRows();
    }

    private async Task RunQuickCheckAsync(CancellationToken cancellationToken)
    {
        IsRunning = true;
        RunSummary = "Checking a few important parts of Windows. Nothing will be changed.";
        HealthScoreText = "Checking";
        HealthScoreDetail = "checking now";
        HealthScoreBrushKey = "AccentTealBrush";

        // Link the command token with a page-owned source so leaving the page cancels the
        // whole run, including the Windows Update probe.
        using CancellationTokenSource runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runCts = runCts;
        CancellationToken token = runCts.Token;
        Task<CommandResult>? updateTask = null;
        try
        {
            foreach ((_, string rowTitle) in FastCheckCommands)
            {
                PageRow? row = Sections[0].Rows.FirstOrDefault(candidate => candidate.Title == rowTitle);
                if (row is not null) ResetRowForCheck(row, "Checking", "Read-only");
            }

            PageRow? wuaRow = Sections[0].Rows.FirstOrDefault(candidate => candidate.Title == WuaRowTitle);
            if (wuaRow is not null)
                ResetRowForCheck(wuaRow, "Checking…", "Checking Windows Update…");

            RebuildResultRowsFromQuickChecks();
            updateTask = RunUpdateCheckAsync(token);
            IReadOnlyList<CommandResult> fastResults = await ParallelCommandProbeRunner.RunPreviewsAsync(
                _dispatcher,
                FastCheckCommands.Select(item => item.CommandId).ToArray(),
                TimeSpan.FromSeconds(3),
                maxConcurrency: 3,
                cancellationToken: token);

            // Discard late results, but settle the cached page through the cancellation path.
            token.ThrowIfCancellationRequested();

            var fastDict = new Dictionary<string, CommandResult>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < FastCheckCommands.Length; index++)
            {
                (string commandId, string rowTitle) = FastCheckCommands[index];
                CommandResult result = fastResults[index];
                fastDict[commandId] = result;
                PageRow? row = Sections[0].Rows.FirstOrDefault(candidate => candidate.Title == rowTitle);
                if (row is null) continue;

                row.State = result.Status == CommandResultStatus.Succeeded ? "Checked" : "Needs review";
                row.Detail = result.Message;
                row.StatusBrushKey = result.Status == CommandResultStatus.Succeeded ? "SuccessBrush" : "WarningBrush";
            }

            EvaluateFindings(fastDict, null);
            RebuildResultRowsFromQuickChecks();

            CommandResult updateResult = await updateTask;
            token.ThrowIfCancellationRequested();
            ApplyWuaResult(updateResult, fastDict);
        }
        catch (OperationCanceledException)
        {
            // Keep the pending state until the update probe has settled in finally.
        }
        catch (Exception ex)
        {
            // This command body is the owning boundary: a late fault in result parsing must
            // become a message instead of escaping to the unhandled-exception handler.
            System.Diagnostics.Debug.WriteLine($"[CheckupPageViewModel] Checkup failed: {ex}");
            CompleteInterruptedCheck(cancelled: false);
        }
        finally
        {
            bool cancelled = token.IsCancellationRequested;
            if (updateTask is not null)
            {
                // Parsing or fast-probe failure must not leave Windows Update orphaned.
                if (!updateTask.IsCompleted) runCts.Cancel();
                await updateTask;
            }
            if (cancelled) CompleteInterruptedCheck(cancelled: true);
            _runCts = null;
            IsRunning = false;
            IsStopping = false;
        }
    }

    private void CompleteInterruptedCheck(bool cancelled)
    {
        foreach (PageRow row in Sections[0].Rows)
        {
            if (row.State is not ("Checking" or "Checking…")) continue;
            row.State = "Not finished";
            row.Detail = cancelled ? "Check stopped. Run Checkup to try again." : "Check failed. Run Checkup to try again.";
            row.StatusBrushKey = "TextSecondaryBrush";
            ClearNavigationAction(row);
        }
        HealthScoreText = cancelled ? "Stopped" : "Incomplete";
        HealthScoreDetail = "completed results are shown below";
        HealthScoreBrushKey = cancelled ? "TextSecondaryBrush" : "WarningBrush";
        RunSummary = cancelled
            ? "Checkup stopped. Nothing was changed. Completed results are kept below; run it again to check all areas."
            : "Checkup couldn't finish. Nothing was changed. Try again, and open Activity if it keeps happening.";
        RebuildResultRowsFromQuickChecks();
    }

    private async Task<CommandResult> RunUpdateCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _dispatcher.ExecuteAsync(
                CommandRequest.Preview(WuaCommandId),
                new CommandExecutionOptions(ReviewApproved: false, Deadline: DateTimeOffset.UtcNow + TimeSpan.FromSeconds(25)),
                cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CheckupPageViewModel] Windows Update check failed: {ex}");
            return new CommandResult(
                WuaCommandId,
                Guid.NewGuid(),
                CommandResultStatus.Failed,
                "wua.query_error",
                "Windows Update couldn't be checked.",
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                false);
        }
    }

    private static void ResetRowForCheck(PageRow row, string state, string detail)
    {
        row.State = state;
        row.Detail = detail;
        row.StatusBrushKey = "AccentTealBrush";
        ClearNavigationAction(row);
    }

    private void RebuildResultRowsFromQuickChecks()
    {
        _resultRows.Clear();
        foreach (PageRow quickRow in Sections[0].Rows)
        {
            _resultRows.Add(new PageRow(quickRow.Title, quickRow.Description, quickRow.State, quickRow.Detail)
            {
                StatusBrushKey = quickRow.StatusBrushKey,
                ActionText = quickRow.ActionText,
                ActionCommand = quickRow.ActionCommand,
                NavigationKey = quickRow.NavigationKey,
                NavigationSectionTitle = quickRow.NavigationSectionTitle,
            });
        }
        ShowResultRows();
    }

    private void ShowResultRows()
    {
        if (SelectedIndex != ResultsSectionIndex) return;

        CurrentRows.Clear();
        foreach (PageRow row in _resultRows)
        {
            row.IsCompact = IsCompactLayout;
            CurrentRows.Add(row);
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private void ApplyWuaResult(CommandResult wuaResult, Dictionary<string, CommandResult> fastDict)
    {
        PageRow? row = Sections[0].Rows.FirstOrDefault(candidate => candidate.Title == WuaRowTitle);
        PageRow? resultRow = _resultRows.FirstOrDefault(candidate => candidate.Title == WuaRowTitle);
        string state = wuaResult.Status == CommandResultStatus.Succeeded ? "Checked" : "Needs review";
        string brushKey = wuaResult.Status == CommandResultStatus.Succeeded ? "SuccessBrush" : "WarningBrush";
        string detail = wuaResult.Message;
        if (wuaResult.Status == CommandResultStatus.Succeeded && wuaResult.Data?.ValueKind == JsonValueKind.Array)
        {
            int count = wuaResult.Data.Value.GetArrayLength();
            if (count > 0) { state = $"{count} updates found"; brushKey = "WarningBrush"; }
            else { state = "Up to date"; brushKey = "SuccessBrush"; }
        }

        ApplyUpdateOutcome(row, state, detail, brushKey);
        ApplyUpdateOutcome(resultRow, state, detail, brushKey);
        EvaluateFindings(fastDict, wuaResult);
        ShowResultRows();
    }

    private static void ApplyUpdateOutcome(PageRow? row, string state, string detail, string brushKey)
    {
        if (row is null) return;
        row.State = state;
        row.Detail = detail;
        row.StatusBrushKey = brushKey;
        if (brushKey == "WarningBrush") SetNavigationAction(row, "Review updates", SystemCareRoute, "Network & updates");
        else ClearNavigationAction(row);
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
                findings.Add($"{rowTitle} check didn't finish");
        }
        if (wuaResult is not null && wuaResult.Status != CommandResultStatus.Succeeded)
            findings.Add("Windows Update check didn't finish");

        if (fastDict.TryGetValue("storage", out CommandResult? storageResult) && storageResult.Status == CommandResultStatus.Succeeded)
        {
            PageRow? storageRow = Sections[0].Rows.FirstOrDefault(candidate => candidate.Title == "Storage");
            if (storageResult.Data?.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement drive in storageResult.Data.Value.EnumerateArray())
                {
                    if (!drive.TryGetProperty("ready", out JsonElement ready) || !ready.GetBoolean() || !drive.TryGetProperty("freeBytes", out JsonElement freeElem))
                        continue;

                    long freeBytes = freeElem.GetInt64();
                    double freeGb = freeBytes / (1024.0 * 1024.0 * 1024.0);
                    string driveName = drive.TryGetProperty("name", out JsonElement nameElem) ? nameElem.GetString() ?? "Drive" : "Drive";
                    if (freeGb < WinCare.Domain.Assessment.AssessmentPolicy.DiskFreeCriticalGb)
                    {
                        hasCritical = true;
                        findings.Add($"Low space on {driveName} ({freeGb:0.0} GB free)");
                        if (storageRow is not null)
                        {
                            storageRow.State = "Very low space";
                            storageRow.StatusBrushKey = "DangerBrush";
                            SetNavigationAction(storageRow, "Review cleanup", SystemCareRoute, "Clean up");
                        }
                    }
                    else if (freeGb < WinCare.Domain.Assessment.AssessmentPolicy.DiskFreeWarningGb)
                    {
                        hasWarning = true;
                        findings.Add($"Low space on {driveName} ({freeGb:0.0} GB free)");
                        if (storageRow is not null && storageRow.StatusBrushKey != "DangerBrush")
                        {
                            storageRow.State = "Low space";
                            storageRow.StatusBrushKey = "WarningBrush";
                            SetNavigationAction(storageRow, "Review cleanup", SystemCareRoute, "Clean up");
                        }
                    }
                }
            }
        }

        if (fastDict.TryGetValue("security", out CommandResult? securityResult) && securityResult.Status == CommandResultStatus.Succeeded)
        {
            PageRow? securityRow = Sections[0].Rows.FirstOrDefault(candidate => candidate.Title == "Security");
            if (securityResult.Data?.ValueKind == JsonValueKind.Object)
            {
                if (securityResult.Data.Value.TryGetProperty("defenderServiceRunning", out JsonElement defenderElement) && !defenderElement.GetBoolean())
                {
                    hasCritical = true;
                    findings.Add("Windows Defender isn't running");
                    if (securityRow is not null)
                    {
                        securityRow.State = "Defender stopped";
                        securityRow.StatusBrushKey = "DangerBrush";
                        SetNavigationAction(securityRow, "Review security", SecurityRoute, "Status");
                    }
                }

                if (securityResult.Data.Value.TryGetProperty("firewallEnabled", out JsonElement firewallElement) && !firewallElement.GetBoolean())
                {
                    hasCritical = true;
                    findings.Add("Firewall is turned off");
                    if (securityRow is not null)
                    {
                        securityRow.State = "Firewall off";
                        securityRow.StatusBrushKey = "DangerBrush";
                        SetNavigationAction(securityRow, "Review security", SecurityRoute, "Status");
                    }
                }
            }
        }

        if (wuaResult is not null && wuaResult.Status == CommandResultStatus.Succeeded && wuaResult.Data?.ValueKind == JsonValueKind.Array)
        {
            int count = wuaResult.Data.Value.GetArrayLength();
            if (count > 0) { hasWarning = true; findings.Add($"{count} updates waiting"); }
        }

        if (hasCritical)
        {
            HealthScoreText = "Action needed";
            HealthScoreDetail = "something needs your attention";
            HealthScoreBrushKey = "DangerBrush";
            RunSummary = $"WinCare found something that needs attention: {string.Join("; ", findings)}.";
        }
        else if (hasWarning)
        {
            HealthScoreText = "Worth a look";
            HealthScoreDetail = "a few things are worth checking";
            HealthScoreBrushKey = "WarningBrush";
            RunSummary = $"A few things are worth a look: {string.Join("; ", findings)}.";
        }
        else if (hasIncompleteProbe)
        {
            HealthScoreText = "Incomplete";
            HealthScoreDetail = "some checks didn't finish";
            HealthScoreBrushKey = "WarningBrush";
            RunSummary = $"Some checks didn't finish: {string.Join("; ", findings)}.";
        }
        else if (updatesPending)
        {
            HealthScoreText = "Checking";
            HealthScoreDetail = "Windows Update is still checking";
            HealthScoreBrushKey = "AccentTealBrush";
            RunSummary = "The main checks are done. Windows Update is still checking.";
        }
        else
        {
            HealthScoreText = "Looks good";
            HealthScoreDetail = "nothing stood out in these checks";
            HealthScoreBrushKey = "SuccessBrush";
            RunSummary = "Everything checked looks okay.";
        }
    }

    private static void SetNavigationAction(PageRow row, string actionText, string navigationKey, string sectionTitle)
    {
        row.ActionText = actionText;
        row.ActionCommand = null;
        row.NavigationKey = navigationKey;
        row.NavigationSectionTitle = sectionTitle;
    }

    private static void ClearNavigationAction(PageRow row)
    {
        row.ActionText = null;
        row.ActionCommand = null;
        row.NavigationKey = null;
        row.NavigationSectionTitle = null;
    }
}
