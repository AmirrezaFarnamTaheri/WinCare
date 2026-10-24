using CommunityToolkit.Mvvm.ComponentModel;
using WinCare.Domain.Activity;
using WinCare.Application.Checkups;

namespace WinCare.App.ViewModels.Pages;

/// <summary>Summarizes recent checkup and activity state. Home never runs system commands.</summary>
public sealed class HomePageViewModel : ObservableObject
{
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

    public void RefreshActivity(IReadOnlyList<ActivityRecord> records, CheckupSessionSnapshot? checkup = null)
    {
        ActivityRecord? latest = records.MaxBy(record => record.StartedAt);
        RecentActivityTitle = latest?.Title ?? "No activity recorded";
        RecentActivitySummary = latest is null
            ? "Your recent WinCare activity will show up here."
            : $"{ToFriendlyState(latest.State)} · {latest.StartedAt.ToLocalTime():g}\n{latest.Result}";
        ActivityStatus = records.Count == 0
            ? "No activity yet"
            : $"{records.Count} record{(records.Count == 1 ? string.Empty : "s")}";

        // Only a single session produced by Checkup can be described as a checkup.
        // Independent command history is still shown as activity above, never blended
        // into a synthetic four-area result.
        if (checkup is null)
        {
            SystemStatus = SecurityStatus = StorageStatus = UpdatesStatus = "Not checked";
            CheckupCoverageText = "0 of 4 areas";
            CheckupTitle = "Start with a checkup";
            CheckupSummary = "It checks a few important areas without changing anything.";
            return;
        }

        SystemStatus = StatusFor(checkup, "system");
        StorageStatus = StatusFor(checkup, "storage");
        SecurityStatus = StatusFor(checkup, "security");
        UpdatesStatus = StatusFor(checkup, "wua-search");

        int completed = checkup.Areas.Count(area => area.State is "Checked" or "NeedsReview");
        int needsReview = checkup.Areas.Count(area => area.State == "NeedsReview");
        CheckupCoverageText = $"{completed} of {CheckupSessionStore.ProbeIds.Length} areas";

        if (checkup.Status == "Running")
        {
            CheckupTitle = "Checkup in progress";
            CheckupSummary = $"{completed} of 4 checks finished. Return to Checkup to see current results.";
        }
        else if (checkup.Status == "Completed")
        {
            DateTimeOffset finishedAt = checkup.CompletedAt ?? checkup.StartedAt;
            if (DateTimeOffset.UtcNow - finishedAt > CheckupFreshnessWindow)
            {
                CheckupTitle = "Checkup results are out of date";
                CheckupSummary = "Run Checkup again to refresh this complete session.";
            }
            else if (needsReview > 0)
            {
                CheckupTitle = "Checkup finished with items to review";
                CheckupSummary = $"{completed} areas checked; {needsReview} need another look.";
            }
            else
            {
                CheckupTitle = "Your latest checkup is ready";
                CheckupSummary = $"Four areas checked in one session at {finishedAt.ToLocalTime():g}.";
            }
        }
        else
        {
            CheckupTitle = checkup.Status == "Cancelled" ? "Checkup stopped" : "Checkup incomplete";
            CheckupSummary = $"{completed} areas checked in the last session. Run Checkup to finish all four.";
        }
    }

    private static string StatusFor(CheckupSessionSnapshot session, string commandId)
    {
        CheckupAreaSnapshot? area = session.Areas.FirstOrDefault(item => item.CommandId == commandId);
        if (area is null) return "Not checked";
        if (area.State is "Checked" or "NeedsReview" &&
            session.Status != "Running" && DateTimeOffset.UtcNow -
            (session.CompletedAt ?? session.StartedAt) > CheckupFreshnessWindow)
            return "Out of date";
        return area.State switch
        {
            "Checked" => "Checked",
            "NeedsReview" => "Needs review",
            "Running" => "Checking…",
            "Pending" => "Not checked yet",
            "Cancelled" => "Check cancelled",
            "Interrupted" => "Not finished",
            _ => "Not checked",
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
