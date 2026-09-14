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

        Assert.Equal("4 of 4 areas", vm.EvidenceScoreText);
        Assert.Equal("Your latest checkup is ready", vm.EvidenceTitle);
        Assert.Equal("Checked", vm.SystemStatus);
        Assert.Equal("Checked", vm.StorageStatus);
        Assert.Equal("Checked", vm.SecurityStatus);
        Assert.Equal("Checked", vm.UpdatesStatus);
    }

    [Fact]
    public void Home_marks_old_completed_checkup_results_as_out_of_date_and_does_not_call_them_ready()
    {
        DateTimeOffset old = DateTimeOffset.UtcNow.AddHours(-2);
        var vm = new HomePageViewModel();
        vm.RefreshActivity([
            Completed("system", "System", old),
            Completed("storage", "Storage", old),
            Completed("security", "Security", old),
            Completed("wua-search", "Updates", old),
        ]);

        Assert.StartsWith("Out of date", vm.SystemStatus);
        Assert.Equal("4 of 4 areas", vm.EvidenceScoreText);
        Assert.Equal("Some checkup results are getting old", vm.EvidenceTitle);
        Assert.Contains("more than 30 minutes old", vm.EvidenceSummary);
    }

    [Fact]
    public void Home_uses_completion_time_for_checkup_freshness()
    {
        DateTimeOffset started = DateTimeOffset.UtcNow.AddHours(-2);
        DateTimeOffset completed = DateTimeOffset.UtcNow.AddMinutes(-2);
        var vm = new HomePageViewModel();
        vm.RefreshActivity([
            Completed("system", "System", started, completed),
            Completed("storage", "Storage", started, completed),
            Completed("security", "Security", started, completed),
            Completed("wua-search", "Updates", started, completed),
        ]);

        Assert.Equal("Your latest checkup is ready", vm.EvidenceTitle);
        Assert.Equal("Checked", vm.SystemStatus);
    }

    private static ActivityRecord Completed(string commandId, string title, DateTimeOffset startedAt, DateTimeOffset? completedAt = null) =>
        new(Guid.NewGuid(), commandId, title, ActivityState.Completed, startedAt, completedAt ?? startedAt.AddSeconds(1), "Evidence collected", false);
}
