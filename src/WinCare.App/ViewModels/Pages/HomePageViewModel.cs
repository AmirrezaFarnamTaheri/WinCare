using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinCare.Application.Commands;
using WinCare.Domain.Activity;
using WinCare.Domain.Commands;
using WinCare.Domain.Telemetry;

namespace WinCare.App.ViewModels.Pages;

public sealed record TelemetryInspectorMetrics(
    long LatencyMicroseconds,
    string TargetPathsSummary,
    float CpuUsagePct = 0f,
    ulong RamUsedBytes = 0,
    ulong RamTotalBytes = 0,
    ulong DiskFreeBytes = 0,
    ulong DiskTotalBytes = 0,
    bool NetActive = false,
    bool CpuAvailable = false)
{
    public string LatencyFormatted => $"{LatencyMicroseconds} µs";
    public string CpuFormatted => CpuAvailable ? $"{CpuUsagePct:F1}%" : "N/A";
    public string RamFormatted => RamTotalBytes > 0
        ? $"{RamUsedBytes / (1024.0 * 1024 * 1024):F1} GB / {RamTotalBytes / (1024.0 * 1024 * 1024):F1} GB"
        : "N/A";
    public string DiskFormatted => DiskTotalBytes > 0
        ? $"{DiskFreeBytes / (1024.0 * 1024 * 1024):F1} GB free"
        : "N/A";
    public string NetFormatted => NetActive ? "Connected" : "Disconnected";
}

public sealed class HomePageViewModel : ObservableObject
{
    private static readonly string[] QuickCheckCommandIds = ["system", "storage", "security", "wua-search"];

    private readonly CommandDispatcher? _dispatcher;
    private readonly Func<CommandDispatcher>? _dispatcherResolver;
    private readonly INativeSystemProbeRepository? _probeRepository;
    private bool _isCompactLayout;
    private bool _isInspectorExpanded;
    private TelemetryInspectorMetrics? _telemetryMetrics;
    private string _recentActivityTitle = "No activity recorded";
    private string _recentActivitySummary = "Checks and reviewed changes will appear here.";
    private string _evidenceScoreText = "0/4";
    private string _evidenceTitle = "No check evidence yet";
    private string _evidenceSummary = "Run a read-only check to collect current evidence.";
    private string _systemStatus = "Not checked";
    private string _securityStatus = "Not checked";
    private string _performanceStatus = "Not checked";
    private string _storageStatus = "Not checked";
    private string _updatesStatus = "Not checked";
    private string _activityStatus = "No activity yet";

    // Curated Action 1: Quick Clean (cleaner-disk-pressure)
    private bool _isCleaning;
    private string _cleanStatusText = "Ready";
    private string _cleanDetailText = "Purge temporary files & free space";
    private string _cleanActionText = "Clean Now";

    // Curated Action 2: Startup Boost (startup)
    private bool _isAuditingStartup;
    private string _startupStatusText = "Ready";
    private string _startupDetailText = "Audit high-impact startup apps";
    private string _startupActionText = "Analyze Startup";

    // Curated Action 3: Network Refresh (network)
    private bool _isRefreshingNetwork;
    private string _networkStatusText = "Ready";
    private string _networkDetailText = "Inspect adapters & network status";
    private string _networkActionText = "Check Network";

    // ponytail: Direct dependency or resolver injection. Avoids coupling ViewModels to AppRuntime.
    public HomePageViewModel(
        CommandDispatcher? dispatcher = null,
        Func<CommandDispatcher>? dispatcherResolver = null,
        INativeSystemProbeRepository? probeRepository = null)
    {
        _dispatcher = dispatcher;
        _dispatcherResolver = dispatcherResolver;
        _probeRepository = probeRepository;
        QuickCleanCommand = new AsyncRelayCommand(QuickCleanAsync);
        StartupBoostCommand = new AsyncRelayCommand(StartupBoostAsync);
        NetworkRefreshCommand = new AsyncRelayCommand(NetworkRefreshAsync);
        ToggleInspectorCommand = new AsyncRelayCommand(ToggleInspectorAsync);
    }

    private CommandDispatcher GetDispatcher()
    {
        if (_dispatcher is not null) return _dispatcher;
        if (_dispatcherResolver is not null) return _dispatcherResolver();
        throw new InvalidOperationException("CommandDispatcher was not provided to HomePageViewModel.");
    }

    public IAsyncRelayCommand QuickCleanCommand { get; }
    public IAsyncRelayCommand StartupBoostCommand { get; }
    public IAsyncRelayCommand NetworkRefreshCommand { get; }
    public IAsyncRelayCommand ToggleInspectorCommand { get; }

    public bool IsInspectorExpanded { get => _isInspectorExpanded; private set => SetProperty(ref _isInspectorExpanded, value); }
    public TelemetryInspectorMetrics? TelemetryMetrics { get => _telemetryMetrics; private set => SetProperty(ref _telemetryMetrics, value); }

    public bool IsCleaning { get => _isCleaning; private set => SetProperty(ref _isCleaning, value); }
    public string CleanStatusText { get => _cleanStatusText; private set => SetProperty(ref _cleanStatusText, value); }
    public string CleanDetailText { get => _cleanDetailText; private set => SetProperty(ref _cleanDetailText, value); }
    public string CleanActionText { get => _cleanActionText; private set => SetProperty(ref _cleanActionText, value); }

    public bool IsAuditingStartup { get => _isAuditingStartup; private set => SetProperty(ref _isAuditingStartup, value); }
    public string StartupStatusText { get => _startupStatusText; private set => SetProperty(ref _startupStatusText, value); }
    public string StartupDetailText { get => _startupDetailText; private set => SetProperty(ref _startupDetailText, value); }
    public string StartupActionText { get => _startupActionText; private set => SetProperty(ref _startupActionText, value); }

    public bool IsRefreshingNetwork { get => _isRefreshingNetwork; private set => SetProperty(ref _isRefreshingNetwork, value); }
    public string NetworkStatusText { get => _networkStatusText; private set => SetProperty(ref _networkStatusText, value); }
    public string NetworkDetailText { get => _networkDetailText; private set => SetProperty(ref _networkDetailText, value); }
    public string NetworkActionText { get => _networkActionText; private set => SetProperty(ref _networkActionText, value); }

    public string RecentActivityTitle { get => _recentActivityTitle; private set => SetProperty(ref _recentActivityTitle, value); }
    public string RecentActivitySummary { get => _recentActivitySummary; private set => SetProperty(ref _recentActivitySummary, value); }
    public string EvidenceScoreText { get => _evidenceScoreText; private set => SetProperty(ref _evidenceScoreText, value); }
    public string EvidenceTitle { get => _evidenceTitle; private set => SetProperty(ref _evidenceTitle, value); }
    public string EvidenceSummary { get => _evidenceSummary; private set => SetProperty(ref _evidenceSummary, value); }
    public string SystemStatus { get => _systemStatus; private set => SetProperty(ref _systemStatus, value); }
    public string SecurityStatus { get => _securityStatus; private set => SetProperty(ref _securityStatus, value); }
    public string PerformanceStatus { get => _performanceStatus; private set => SetProperty(ref _performanceStatus, value); }
    public string StorageStatus { get => _storageStatus; private set => SetProperty(ref _storageStatus, value); }
    public string UpdatesStatus { get => _updatesStatus; private set => SetProperty(ref _updatesStatus, value); }
    public string ActivityStatus { get => _activityStatus; private set => SetProperty(ref _activityStatus, value); }

    private async Task QuickCleanAsync(CancellationToken cancellationToken)
    {
        if (IsCleaning) return;
        IsCleaning = true;
        CleanStatusText = "Cleaning…";
        CleanDetailText = "Purging expired temporary files…";

        try
        {
            CommandDispatcher dispatcher = GetDispatcher();
            using JsonDocument doc = JsonDocument.Parse("{}");
            CommandResult result = await dispatcher.ExecuteAsync(
                CommandRequest.Execute("cleaner-disk-pressure", doc.RootElement),
                new CommandExecutionOptions(ReviewApproved: false),
                cancellationToken);

            CleanStatusText = result.Status == CommandResultStatus.Succeeded ? "Clean Complete" : "Failed";
            CleanDetailText = !string.IsNullOrWhiteSpace(result.Message) ? result.Message : "Cleanup operation finished.";
            CleanActionText = "Clean Again";
        }
        catch (Exception ex)
        {
            CleanStatusText = "Error";
            CleanDetailText = ex.Message;
        }
        finally
        {
            IsCleaning = false;
        }
    }

    private async Task StartupBoostAsync(CancellationToken cancellationToken)
    {
        if (IsAuditingStartup) return;
        IsAuditingStartup = true;
        StartupStatusText = "Analyzing…";
        StartupDetailText = "Inspecting startup items…";

        try
        {
            CommandDispatcher dispatcher = GetDispatcher();
            CommandResult result = await dispatcher.ExecuteAsync(
                CommandRequest.Preview("startup"),
                CommandExecutionOptions.Default,
                cancellationToken);

            StartupStatusText = result.Status == CommandResultStatus.Succeeded ? "Audit Complete" : "Failed";
            StartupDetailText = !string.IsNullOrWhiteSpace(result.Message) ? result.Message : "Startup analysis completed.";
            StartupActionText = "Re-analyze";
        }
        catch (Exception ex)
        {
            StartupStatusText = "Error";
            StartupDetailText = ex.Message;
        }
        finally
        {
            IsAuditingStartup = false;
        }
    }

    private async Task NetworkRefreshAsync(CancellationToken cancellationToken)
    {
        if (IsRefreshingNetwork) return;
        IsRefreshingNetwork = true;
        NetworkStatusText = "Checking…";
        NetworkDetailText = "Querying network interfaces…";

        try
        {
            CommandDispatcher dispatcher = GetDispatcher();
            CommandResult result = await dispatcher.ExecuteAsync(
                CommandRequest.Preview("network"),
                CommandExecutionOptions.Default,
                cancellationToken);

            NetworkStatusText = result.Status == CommandResultStatus.Succeeded ? "Connected" : "Failed";
            NetworkDetailText = !string.IsNullOrWhiteSpace(result.Message) ? result.Message : "Network query completed.";
            NetworkActionText = "Check Again";
        }
        catch (Exception ex)
        {
            NetworkStatusText = "Error";
            NetworkDetailText = ex.Message;
        }
        finally
        {
            IsRefreshingNetwork = false;
        }
    }

    private async Task ToggleInspectorAsync(CancellationToken cancellationToken)
    {
        if (IsInspectorExpanded)
        {
            IsInspectorExpanded = false;
            return;
        }

        IsInspectorExpanded = true;

        float cpu = 0f;
        bool cpuAvailable = false;
        ulong ramUsed = 0, ramTotal = 0, diskFree = 0, diskTotal = 0;
        bool netActive = false;

        long start = Stopwatch.GetTimestamp();
        if (_probeRepository is not null)
        {
            try
            {
                SystemSnapshot snapshot = await _probeRepository.GetSystemSnapshotAsync(cancellationToken);
                cpu = snapshot.CpuUsagePct;
                cpuAvailable = true;
                ramUsed = snapshot.RamUsedBytes;
                ramTotal = snapshot.RamTotalBytes;
                diskFree = snapshot.DiskFreeBytes;
                diskTotal = snapshot.DiskTotalBytes;
                netActive = snapshot.NetActive;
            }
            catch
            {
                // Keep metrics unavailable on probe fault rather than inventing live values.
            }
        }
        long latencyUs = (long)Stopwatch.GetElapsedTime(start).TotalMicroseconds;

        TelemetryMetrics = new TelemetryInspectorMetrics(
            LatencyMicroseconds: latencyUs,
            TargetPathsSummary: "%TEMP% / LocalAppData\\Temp (expired files)",
            CpuUsagePct: cpu,
            RamUsedBytes: ramUsed,
            RamTotalBytes: ramTotal,
            DiskFreeBytes: diskFree,
            DiskTotalBytes: diskTotal,
            NetActive: netActive,
            CpuAvailable: cpuAvailable);
    }

    public void RefreshActivity(IReadOnlyList<ActivityRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        ActivityRecord? latest = records.MaxBy(record => record.StartedAt);
        RecentActivityTitle = latest?.Title ?? "No activity recorded";
        RecentActivitySummary = latest is null
            ? "Checks and reviewed changes will appear here."
            : $"{ToFriendlyState(latest.State)} · {latest.StartedAt.ToLocalTime():g}\n{latest.Result}";
        ActivityStatus = records.Count == 0
            ? "No activity yet"
            : $"{records.Count} record{(records.Count == 1 ? string.Empty : "s")}";

        var latestByCommand = records
            .Where(record => QuickCheckCommandIds.Contains(record.CommandId, StringComparer.OrdinalIgnoreCase))
            .GroupBy(record => record.CommandId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.MaxBy(record => record.StartedAt)!, StringComparer.OrdinalIgnoreCase);

        SystemStatus = StatusFor(latestByCommand, "system");
        StorageStatus = StatusFor(latestByCommand, "storage");
        SecurityStatus = StatusFor(latestByCommand, "security");
        UpdatesStatus = StatusFor(latestByCommand, "wua-search");
        // The current quick check's Windows/hardware probe includes processor and memory evidence.
        // Label this as evidence collection rather than a performance-health diagnosis.
        PerformanceStatus = StatusFor(latestByCommand, "system");

        int collected = QuickCheckCommandIds.Count(commandId =>
            latestByCommand.TryGetValue(commandId, out ActivityRecord? record) &&
            record.State == ActivityState.Completed);
        int needsReview = QuickCheckCommandIds.Count(commandId =>
            latestByCommand.TryGetValue(commandId, out ActivityRecord? record) &&
            record.State is ActivityState.Failed or ActivityState.NeedsAttention);

        EvidenceScoreText = $"{collected}/{QuickCheckCommandIds.Length}";
        if (collected == QuickCheckCommandIds.Length)
        {
            DateTimeOffset newestCheck = latestByCommand.Values.Max(record => record.StartedAt);
            EvidenceTitle = "Latest check evidence collected";
            EvidenceSummary = $"All four read-only probes reported an outcome. Last evidence: {newestCheck.ToLocalTime():g}. Review category details before acting.";
        }
        else if (latestByCommand.Count > 0)
        {
            EvidenceTitle = needsReview > 0 ? "Check evidence needs review" : "Check evidence is incomplete";
            EvidenceSummary = $"{collected} of {QuickCheckCommandIds.Length} read-only probes completed. This is evidence coverage, not a machine-health score.";
        }
        else
        {
            EvidenceTitle = "No check evidence yet";
            EvidenceSummary = "Run a read-only check to collect current evidence before WinCare recommends anything.";
        }
    }

    public bool IsCompactLayout
    {
        get => _isCompactLayout;
        private set => SetProperty(ref _isCompactLayout, value);
    }

    public void SetCompactLayout(bool isCompact) => IsCompactLayout = isCompact;

    private static string StatusFor(IReadOnlyDictionary<string, ActivityRecord> latestByCommand, string commandId)
    {
        if (!latestByCommand.TryGetValue(commandId, out ActivityRecord? record))
        {
            return "Not checked";
        }

        return record.State switch
        {
            ActivityState.Completed => "Evidence collected",
            ActivityState.Running => "Checking…",
            ActivityState.NeedsAttention => "Needs review",
            ActivityState.Failed => "Check failed",
            ActivityState.Cancelled => "Check cancelled",
            _ => ToFriendlyState(record.State),
        };
    }

    private static string ToFriendlyState(ActivityState state) => state switch
    {
        ActivityState.NeedsAttention => "Needs attention",
        ActivityState.Completed => "Completed",
        ActivityState.Cancelled => "Cancelled",
        ActivityState.Failed => "Failed",
        ActivityState.Running => "Running",
        _ => state.ToString(),
    };
}
