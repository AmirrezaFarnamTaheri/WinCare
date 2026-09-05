using CommunityToolkit.Mvvm.ComponentModel;
using WinCare.Domain.Activity;

namespace WinCare.App.ViewModels.Pages;

public sealed class HomePageViewModel : ObservableObject
{
    private bool _isCompactLayout;
    private string _recentActivityTitle = "No activity recorded";
    private string _recentActivitySummary = "Checks, reviewed changes, and recovery receipts will appear here.";

    public string RecentActivityTitle { get => _recentActivityTitle; private set => SetProperty(ref _recentActivityTitle, value); }
    public string RecentActivitySummary { get => _recentActivitySummary; private set => SetProperty(ref _recentActivitySummary, value); }

    public void RefreshActivity(IReadOnlyList<ActivityRecord> records)
    {
        var latest = records.MaxBy(record => record.StartedAt);
        RecentActivityTitle = latest?.Title ?? "No activity recorded";
        RecentActivitySummary = latest is null
            ? "Checks, reviewed changes, and recovery receipts will appear here."
            : $"{latest.State} · {latest.StartedAt.ToLocalTime():g}\n{latest.Result}";
    }

    public bool IsCompactLayout
    {
        get => _isCompactLayout;
        private set => SetProperty(ref _isCompactLayout, value);
    }

    public void SetCompactLayout(bool isCompact) => IsCompactLayout = isCompact;
}
