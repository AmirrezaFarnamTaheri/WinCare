using WinCare.Infrastructure.Commands;

namespace WinCare.Infrastructure.Tests;

public sealed class MsiPackageHelperTests
{
    [Fact]
    public void GuidToSquid_and_back_round_trips_correctly()
    {
        Guid original = Guid.NewGuid();
        string squid = WindowsCommandExecutor.MsiPackageHelper.GuidToSquid(original);

        Assert.Equal(32, squid.Length);
        Assert.True(WindowsCommandExecutor.MsiPackageHelper.TrySquidToGuid(squid, out Guid roundTripped));
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void Known_guid_matches_expected_squid()
    {
        // GUID: {B902A240-6254-47F6-8B2B-6C35F92EAA27}
        var guid = Guid.Parse("B902A240-6254-47F6-8B2B-6C35F92EAA27");
        string squid = WindowsCommandExecutor.MsiPackageHelper.GuidToSquid(guid);

        // Verification: reverse blocks match SQUID algorithm
        Assert.StartsWith("042A209B45266F74", squid);
        Assert.True(WindowsCommandExecutor.MsiPackageHelper.TrySquidToGuid(squid, out Guid parsed));
        Assert.Equal(guid, parsed);
    }

    [Fact]
    public void Invalid_squid_returns_false()
    {
        Assert.False(WindowsCommandExecutor.MsiPackageHelper.TrySquidToGuid(string.Empty, out _));
        Assert.False(WindowsCommandExecutor.MsiPackageHelper.TrySquidToGuid("too_short", out _));
        Assert.False(WindowsCommandExecutor.MsiPackageHelper.TrySquidToGuid("invalid_hex_string_with_thirty_two_chars_!", out _));
    }
}