using WinCare.App.ViewModels.Pages;
using WinCare.Application.Checkups;
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
    public void Home_refuses_to_combine_unrelated_activity_into_a_fake_checkup()
    {
        var vm = new HomePageViewModel();
        vm.RefreshActivity(CheckupSessionStore.ProbeIds.Select(id =>
            new ActivityRecord(Guid.NewGuid(), id, id, ActivityState.Completed,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Done", false)).ToArray());

        Assert.Equal("0 of 4 areas", vm.CheckupCoverageText);
        Assert.Equal("Start with a checkup", vm.CheckupTitle);
        Assert.Equal("Not checked", vm.SystemStatus);
    }

    [Fact]
    public void Home_projects_exact_coverage_from_a_single_completed_session()
    {
        var vm = new HomePageViewModel();
        vm.RefreshActivity([], Session(DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow, "Completed"));

        Assert.Equal("4 of 4 areas", vm.CheckupCoverageText);
        Assert.Equal("Your latest checkup is ready", vm.CheckupTitle);
        Assert.Equal("Checked", vm.SystemStatus);
        Assert.Equal("Checked", vm.StorageStatus);
        Assert.Equal("Checked", vm.SecurityStatus);
        Assert.Equal("Checked", vm.UpdatesStatus);
    }

    [Fact]
    public void Home_marks_a_completed_old_session_as_out_of_date()
    {
        var vm = new HomePageViewModel();
        DateTimeOffset old = DateTimeOffset.UtcNow.AddHours(-2);
        vm.RefreshActivity([], Session(old.AddMinutes(-1), old, "Completed"));

        Assert.Equal("Out of date", vm.SystemStatus);
        Assert.Equal("4 of 4 areas", vm.CheckupCoverageText);
        Assert.Equal("Checkup results are out of date", vm.CheckupTitle);
        Assert.Contains("refresh", vm.CheckupSummary);
    }

    [Fact]
    public void Home_uses_the_session_completion_not_start_time_for_freshness()
    {
        var vm = new HomePageViewModel();
        vm.RefreshActivity([], Session(
            DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddMinutes(-2), "Completed"));

        Assert.Equal("Your latest checkup is ready", vm.CheckupTitle);
        Assert.Equal("Checked", vm.SystemStatus);
    }

    [Theory]
    [InlineData("Incomplete", "Checkup incomplete")]
    [InlineData("Cancelled", "Checkup stopped")]
    [InlineData("Interrupted", "Checkup incomplete")]
    public void Home_reports_nonrunning_partial_sessions_truthfully(string state, string title)
    {
        var vm = new HomePageViewModel();
        var session = Session(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow, state)
            with { Areas = Areas("Checked", "Interrupted", "Interrupted", "Interrupted") };
        vm.RefreshActivity([], session);

        Assert.Equal(title, vm.CheckupTitle);
        Assert.Equal("1 of 4 areas", vm.CheckupCoverageText);
        Assert.Contains("Run Checkup", vm.CheckupSummary);
    }

    [Fact]
    public void Home_reports_only_an_active_session_as_running()
    {
        var vm = new HomePageViewModel();
        var session = Session(DateTimeOffset.UtcNow, null, "Running")
            with { Areas = Areas("Checked", "Running", "Running", "Running") };
        vm.RefreshActivity([], session);

        Assert.Equal("Checkup in progress", vm.CheckupTitle);
        Assert.Equal("1 of 4 areas", vm.CheckupCoverageText);
        Assert.Equal("Checking…", vm.StorageStatus);
    }

    private static CheckupSessionSnapshot Session(DateTimeOffset started, DateTimeOffset? completed, string state) =>
        new(1, Guid.NewGuid(), started, completed, state, Areas("Checked", "Checked", "Checked", "Checked"));

    private static CheckupAreaSnapshot[] Areas(string system, string storage, string security, string updates) =>
    [
        new("system", system),
        new("storage", storage),
        new("security", security),
        new("wua-search", updates),
    ];
}
