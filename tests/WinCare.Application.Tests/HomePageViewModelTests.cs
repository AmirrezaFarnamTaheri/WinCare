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
            Result = "Check completed"
        };

        vm.RefreshActivity([recent, old]);

        Assert.Equal("Latest check", vm.RecentActivityTitle);
        Assert.Contains("Check completed", vm.RecentActivitySummary);
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

        Assert.Equal("4 of 4 areas", vm.CheckupCoverageText);
        Assert.Equal("Your latest checkup is ready", vm.CheckupTitle);
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
        Assert.Equal("4 of 4 areas", vm.CheckupCoverageText);
        Assert.Equal("Your checkup is getting old", vm.CheckupTitle);
        Assert.Contains("over 30 minutes old", vm.CheckupSummary);
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

        Assert.Equal("Your latest checkup is ready", vm.CheckupTitle);
        Assert.Equal("Checked", vm.SystemStatus);
    }

    [Theory]
    [InlineData(ActivityState.Completed)]
    [InlineData(ActivityState.Cancelled)]
    public void Home_calls_inactive_partial_checks_incomplete(ActivityState state)
    {
        var vm = new HomePageViewModel();
        var record = Completed("system", "System", DateTimeOffset.UtcNow.AddMinutes(-1)) with { State = state };

        vm.RefreshActivity([record]);

        Assert.Equal("Checkup is incomplete", vm.CheckupTitle);
        Assert.Contains("Run Checkup", vm.CheckupSummary);
    }

    [Fact]
    public void Home_calls_only_running_checks_in_progress()
    {
        var vm = new HomePageViewModel();
        var record = Completed("system", "System", DateTimeOffset.UtcNow) with
        {
            State = ActivityState.Running,
            CompletedAt = null
        };

        vm.RefreshActivity([record]);

        Assert.Equal("Checkup is still in progress", vm.CheckupTitle);
    }

    private static ActivityRecord Completed(string commandId, string title, DateTimeOffset startedAt, DateTimeOffset? completedAt = null) =>
        new(Guid.NewGuid(), commandId, title, ActivityState.Completed, startedAt, completedAt ?? startedAt.AddSeconds(1), "Check completed", false);
}
