using CommunityToolkit.Mvvm.ComponentModel;
using WinCare.Domain.Activity;

namespace WinCare.App.ViewModels.Pages;

/// <summary>Summarizes recent checkup and activity state. Home never runs system commands.</summary>
public sealed class HomePageViewModel : ObservableObject
{
    private static readonly string[] QuickCheckCommandIds = ["system", "storage", "security", "wua-search"];
    private static readonly TimeSpan CheckupFreshnessWindow = TimeSpan.FromMinutes(30);

    private bool _isCompactLayout;
    private string _recentActivityTitle = "No activity recorded";
    private string _recentActivitySummary = "Your recent WinCare activity will show up here.";
    private string _checkupCoverageText = "0 of 4 areas";
    private string _checkupTitle = "No recent checkup yet";
    private string _checkupSummary = "Run Checkup to see the latest results.";
    private string _systemStatus = "Not checked";
    private string _securityStatus = "Not checked";
    private string _storageStatus = "Not checked";
    private string _updatesStatus = "Not checked";
    private string _activityStatus = "No activity yet";

    public string RecentActivityTitle { get => _recentActivityTitle; private set => SetProperty(ref _recentActivityTitle, value); }
    public string RecentActivitySummary { get => _recentActivitySummary; private set => SetProperty(ref _recentActivitySummary, value); }
    public string CheckupCoverageText { get => _checkupCoverageText; private set => SetProperty(ref _checkupCoverageText, value); }
    public string CheckupTitle { get => _checkupTitle; private set => SetProperty(ref _checkupTitle, value); }
    public string CheckupSummary { get => _checkupSummary; private set => SetProperty(ref _checkupSummary, value); }
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
        ActivityRecord? latest = records.MaxBy(record => record.StartedAt);
        RecentActivityTitle = latest?.Title ?? "No activity recorded";
        RecentActivitySummary = latest is null
            ? "Your recent WinCare activity will show up here."
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
        int completed = QuickCheckCommandIds.Count(commandId =>
            latestByCommand.TryGetValue(commandId, out ActivityRecord? record) &&
            record.State == ActivityState.Completed);
        int recent = QuickCheckCommandIds.Count(commandId =>
            latestByCommand.TryGetValue(commandId, out ActivityRecord? record) &&
            record.State == ActivityState.Completed &&
            now - CheckupTimestamp(record) <= CheckupFreshnessWindow);
        int needsAttention = QuickCheckCommandIds.Count(commandId =>
            latestByCommand.TryGetValue(commandId, out ActivityRecord? record) &&
            record.State is ActivityState.Failed or ActivityState.NeedsAttention);

        CheckupCoverageText = $"{completed} of {QuickCheckCommandIds.Length} areas";
        if (recent == QuickCheckCommandIds.Length)
        {
            DateTimeOffset oldestResult = latestByCommand.Values.Min(CheckupTimestamp);
            CheckupTitle = "Your latest checkup is ready";
            CheckupSummary = $"All four areas are up to date. Oldest result: {oldestResult.ToLocalTime():g}.";
        }
        else if (completed == QuickCheckCommandIds.Length)
        {
            CheckupTitle = "Your checkup is getting old";
            CheckupSummary = recent == 0
                ? "These results are over 30 minutes old. Run Checkup for a fresh look."
                : $"{recent} of {QuickCheckCommandIds.Length} results are still recent. Run Checkup to refresh the rest.";
        }
        else if (latestByCommand.Count > 0)
        {
            CheckupTitle = needsAttention > 0 ? "A few checks need attention" : "Checkup is still in progress";
            CheckupSummary = $"{completed} of {QuickCheckCommandIds.Length} checks finished. Open a result for details.";
        }
        else
        {
            CheckupTitle = "Start with a checkup";
            CheckupSummary = "It checks a few important areas without changing anything.";
        }
    }

    private static string StatusFor(IReadOnlyDictionary<string, ActivityRecord> latestByCommand, string commandId)
    {
        if (!latestByCommand.TryGetValue(commandId, out ActivityRecord? record)) return "Not checked";

        if (record.State == ActivityState.Completed && DateTimeOffset.UtcNow - CheckupTimestamp(record) > CheckupFreshnessWindow)
            return $"Out of date ({CheckupTimestamp(record).ToLocalTime():HH:mm})";

        return record.State switch
        {
            ActivityState.Completed => "Checked",
            ActivityState.Running => "Checking…",
            ActivityState.NeedsAttention => "Needs attention",
            ActivityState.Failed => "Check failed",
            ActivityState.Cancelled => "Check cancelled",
            _ => ToFriendlyState(record.State),
        };
    }

    private static DateTimeOffset CheckupTimestamp(ActivityRecord record) => record.CompletedAt ?? record.StartedAt;

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
