using CommunityToolkit.Mvvm.ComponentModel;
using WinCare.Domain.Activity;

namespace WinCare.App.ViewModels.Pages;

public sealed class HomePageViewModel : ObservableObject
{
    private static readonly string[] QuickCheckCommandIds = ["system", "storage", "security", "wua-search"];

    private bool _isCompactLayout;
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
