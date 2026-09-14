using CommunityToolkit.Mvvm.ComponentModel;
using WinCare.Domain.Activity;

namespace WinCare.App.ViewModels.Pages;

/// <summary>
/// Presentation-only projection for the Home surface. Home summarizes existing evidence
/// and routes users to dedicated workflows; it does not execute system commands itself.
/// </summary>
public sealed class HomePageViewModel : ObservableObject
{
    private static readonly string[] QuickCheckCommandIds = ["system", "storage", "security", "wua-search"];
    private static readonly TimeSpan EvidenceFreshnessWindow = TimeSpan.FromMinutes(30);

    private bool _isCompactLayout;
    private string _recentActivityTitle = "No activity recorded";
    private string _recentActivitySummary = "Checks and reviewed changes will appear here.";
    private string _evidenceScoreText = "0 of 4 areas";
    private string _evidenceTitle = "No recent checkup yet";
    private string _evidenceSummary = "Run a read-only checkup to see the latest results.";
    private string _systemStatus = "Not checked";
    private string _securityStatus = "Not checked";
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
    public string StorageStatus { get => _storageStatus; private set => SetProperty(ref _storageStatus, value); }
    public string UpdatesStatus { get => _updatesStatus; private set => SetProperty(ref _updatesStatus, value); }
    public string ActivityStatus { get => _activityStatus; private set => SetProperty(ref _activityStatus, value); }

    public bool IsCompactLayout
    {
        get => _isCompactLayout;
        private set => SetProperty(ref _isCompactLayout, value);
    }

    public void SetCompactLayout(bool isCompact) => IsCompactLayout = isCompact;

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

        DateTimeOffset now = DateTimeOffset.UtcNow;
        int collected = QuickCheckCommandIds.Count(commandId =>
            latestByCommand.TryGetValue(commandId, out ActivityRecord? record) &&
            record.State == ActivityState.Completed);
        int freshCollected = QuickCheckCommandIds.Count(commandId =>
            latestByCommand.TryGetValue(commandId, out ActivityRecord? record) &&
            record.State == ActivityState.Completed &&
            now - EvidenceTimestamp(record) <= EvidenceFreshnessWindow);
        int needsReview = QuickCheckCommandIds.Count(commandId =>
            latestByCommand.TryGetValue(commandId, out ActivityRecord? record) &&
            record.State is ActivityState.Failed or ActivityState.NeedsAttention);

        EvidenceScoreText = $"{collected} of {QuickCheckCommandIds.Length} areas";
        if (freshCollected == QuickCheckCommandIds.Length)
        {
            DateTimeOffset oldestResult = latestByCommand.Values.Min(EvidenceTimestamp);
            EvidenceTitle = "Your latest checkup is ready";
            EvidenceSummary = $"All four areas have recent results. Oldest check: {oldestResult.ToLocalTime():g}. Open an area below for details and next steps.";
        }
        else if (collected == QuickCheckCommandIds.Length)
        {
            EvidenceTitle = "Some checkup results are getting old";
            EvidenceSummary = freshCollected == 0
                ? "The latest results in all four areas are more than 30 minutes old. Run Checkup for a current snapshot."
                : $"{freshCollected} of {QuickCheckCommandIds.Length} areas still have recent results. Run Checkup to refresh the rest.";
        }
        else if (latestByCommand.Count > 0)
        {
            EvidenceTitle = needsReview > 0 ? "Some checks need your attention" : "Your PC snapshot is taking shape";
            EvidenceSummary = $"{collected} of {QuickCheckCommandIds.Length} areas have completed checks. Open a result to see what WinCare found.";
        }
        else
        {
            EvidenceTitle = "Start with a fresh PC snapshot";
            EvidenceSummary = "Run a read-only checkup to see what is happening before WinCare suggests a next step.";
        }
    }

    private static string StatusFor(IReadOnlyDictionary<string, ActivityRecord> latestByCommand, string commandId)
    {
        if (!latestByCommand.TryGetValue(commandId, out ActivityRecord? record))
            return "Not checked";

        if (record.State == ActivityState.Completed && DateTimeOffset.UtcNow - EvidenceTimestamp(record) > EvidenceFreshnessWindow)
            return $"Out of date ({EvidenceTimestamp(record).ToLocalTime():HH:mm})";

        return record.State switch
        {
            ActivityState.Completed => "Checked",
            ActivityState.Running => "Checking…",
            ActivityState.NeedsAttention => "Needs review",
            ActivityState.Failed => "Check failed",
            ActivityState.Cancelled => "Check cancelled",
            _ => ToFriendlyState(record.State),
        };
    }

    private static DateTimeOffset EvidenceTimestamp(ActivityRecord record) => record.CompletedAt ?? record.StartedAt;

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
