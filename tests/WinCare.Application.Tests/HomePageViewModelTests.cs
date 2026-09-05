using WinCare.App.ViewModels.Pages;
using WinCare.Domain.Activity;

namespace WinCare.Application.Tests;

public sealed class HomePageViewModelTests
{
    [Fact]
    public void Dashboard_shows_latest_activity_and_restores_empty_state()
    {
        var vm = new HomePageViewModel();
        var old = new ActivityRecord(Guid.NewGuid(), "old", "Old check", ActivityState.Completed,
            DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, "Old result", false);
        var recent = old with { Id = Guid.NewGuid(), Title = "Latest check", StartedAt = DateTimeOffset.UtcNow, Result = "Evidence collected" };
        vm.RefreshActivity([recent, old]);
        Assert.Equal("Latest check", vm.RecentActivityTitle);
        Assert.Contains("Evidence collected", vm.RecentActivitySummary);
        vm.RefreshActivity([]);
        Assert.Equal("No activity recorded", vm.RecentActivityTitle);
    }
}
