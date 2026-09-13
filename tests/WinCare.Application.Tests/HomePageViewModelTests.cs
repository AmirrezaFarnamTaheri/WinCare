using WinCare.App.ViewModels.Pages;
using WinCare.Domain.Activity;

namespace WinCare.Application.Tests;

public sealed class HomePageViewModelTests
{
    [Fact]
    public void Home_shows_latest_activity_and_restores_empty_state()
    {
        var vm = new HomePageViewModel();
        var old = new ActivityRecord(Guid.NewGuid(), "old", "Old check", ActivityState.Completed,
            DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, "Old result", false);
        var recent = old with
        {
            Id = Guid.NewGuid(),
            Title = "Latest check",
            StartedAt = DateTimeOffset.UtcNow,
            Result = "Evidence collected"
        };

        vm.RefreshActivity([recent, old]);

        Assert.Equal("Latest check", vm.RecentActivityTitle);
        Assert.Contains("Evidence collected", vm.RecentActivitySummary);
        Assert.Equal("2 records", vm.ActivityStatus);

        vm.RefreshActivity([]);

        Assert.Equal("No activity recorded", vm.RecentActivityTitle);
        Assert.Equal("No activity yet", vm.ActivityStatus);
    }

    [Fact]
    public void Home_projects_exact_checkup_coverage_without_duplicate_status_signals()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var vm = new HomePageViewModel();
        vm.RefreshActivity([
            Completed("system", "System", now),
            Completed("storage", "Storage", now),
            Completed("security", "Security", now),
            Completed("wua-search", "Updates", now),
        ]);

        Assert.Equal("4/4", vm.EvidenceScoreText);
        Assert.Equal("Your latest check is ready", vm.EvidenceTitle);
        Assert.Equal("Evidence collected", vm.SystemStatus);
        Assert.Equal("Evidence collected", vm.StorageStatus);
        Assert.Equal("Evidence collected", vm.SecurityStatus);
        Assert.Equal("Evidence collected", vm.UpdatesStatus);
    }

    [Fact]
    public void Home_marks_old_completed_evidence_as_stale()
    {
        var vm = new HomePageViewModel();
        vm.RefreshActivity([Completed("system", "System", DateTimeOffset.UtcNow.AddHours(-2))]);

        Assert.StartsWith("Stale evidence", vm.SystemStatus);
        Assert.Equal("1/4", vm.EvidenceScoreText);
    }

    private static ActivityRecord Completed(string commandId, string title, DateTimeOffset startedAt) =>
        new(Guid.NewGuid(), commandId, title, ActivityState.Completed, startedAt, startedAt.AddSeconds(1), "Evidence collected", false);
}
