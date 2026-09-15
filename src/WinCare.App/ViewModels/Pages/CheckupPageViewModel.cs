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
    private readonly List<PageRow> _resultRows = [];
    private bool _isRunning;
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

    public string RunActionText => IsRunning ? "Checking your PC…" : "Run checkup";
    public string RunSummary { get => _runSummary; private set => SetProperty(ref _runSummary, value); }
    public string HealthScoreText { get => _healthScoreText; private set => SetProperty(ref _healthScoreText, value); }
    public string HealthScoreDetail { get => _healthScoreDetail; private set => SetProperty(ref _healthScoreDetail, value); }
    public string HealthScoreBrushKey { get => _healthScoreBrushKey; private set => SetProperty(ref _healthScoreBrushKey, value); }

    public override void SelectSection(int index)
    {
        base.SelectSection(index);
        if (index == ResultsSectionIndex) ShowResultRows();
    }

    private async Task RunQuickCheckAsync()
    {
        IsRunning = true;
        RunSummary = "Checking a few important parts of Windows. Nothing will be changed.";
        HealthScoreText = "Checking";
        HealthScoreDetail = "checking now";
        HealthScoreBrushKey = "AccentTealBrush";

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

            Task<CommandResult> updateTask = RunUpdateCheckAsync();
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
                if (row is null) continue;

                row.State = result.Status == CommandResultStatus.Succeeded ? "Checked" : "Needs review";
                row.Detail = result.Message;
                row.StatusBrushKey = result.Status == CommandResultStatus.Succeeded ? "SuccessBrush" : "WarningBrush";
            }

            EvaluateFindings(fastDict, null);
            RebuildResultRowsFromQuickChecks();

            CommandResult updateResult = await updateTask;
            ApplyWuaResult(updateResult, fastDict);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task<CommandResult> RunUpdateCheckAsync()
    {
        try
        {
            return await _dispatcher.ExecuteAsync(
                CommandRequest.Preview(WuaCommandId),
                new CommandExecutionOptions(ReviewApproved: false, Deadline: DateTimeOffset.UtcNow + TimeSpan.FromSeconds(25)),
                CancellationToken.None);
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
        if (brushKey == "WarningBrush") SetNavigationAction(row, "Review updates", "system-care", "Network & updates");
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
                            SetNavigationAction(storageRow, "Review cleanup", "system-care", "Clean up");
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
                            SetNavigationAction(storageRow, "Review cleanup", "system-care", "Clean up");
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
                        SetNavigationAction(securityRow, "Review security", "security", "Status");
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
                        SetNavigationAction(securityRow, "Review security", "security", "Status");
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
