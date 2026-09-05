using System.Text.Json;
using WinCare.App.Services;

namespace WinCare.Application.Tests;

public sealed class AppPreferenceDataTests
{
    [Fact]
    public void Null_persisted_lists_are_recovered_without_touching_user_settings()
    {
        var data = JsonSerializer.Deserialize<AppPreferenceData>("""
            {"Theme":null,"FavoriteCommandIds":null,"RecentCommandIds":null}
            """)!.Normalize();
        Assert.Equal("System", data.Theme);
        Assert.Empty(data.FavoriteCommandIds);
        Assert.Empty(data.RecentCommandIds);
    }

    [Fact]
    public void Recent_history_is_bounded_and_case_insensitive()
    {
        var data = new AppPreferenceData
        {
            RecentCommandIds = ["system", "SYSTEM", "", .. Enumerable.Range(0, 30).Select(i => "tool" + i)],
        }.Normalize();
        Assert.Equal(20, data.RecentCommandIds.Count);
        Assert.Equal("system", data.RecentCommandIds[0]);
        Assert.DoesNotContain("SYSTEM", data.RecentCommandIds);
        Assert.DoesNotContain("", data.RecentCommandIds);
    }
}
