using WinCare.Infrastructure.Commands;

namespace WinCare.Infrastructure.Tests;

public sealed class DefenderServiceStateTests
{
    [Theory]
    [InlineData(0, "SERVICE_NAME: WinDefend\n        STATE : 4 RUNNING", "Running")]
    [InlineData(0, "SERVICE_NAME: WinDefend\n        STATE : 1 STOPPED", "Stopped")]
    [InlineData(0, "SERVICE_NAME: WinDefend\n        STATE : 2 START_PENDING", "Transitioning")]
    [InlineData(1060, "The specified service does not exist", "Unknown")]
    [InlineData(5, "Access is denied", "Unknown")]
    [InlineData(0, "The service state could not be parsed", "Unknown")]
    public void Classifies_service_state_without_treating_failed_probes_as_stopped(
        int exitCode, string output, string expected)
    {
        Assert.Equal(expected, WindowsCommandExecutor.ClassifyDefenderServiceState(exitCode, output));
    }

    [Fact]
    public void Firewall_state_is_unknown_when_any_profile_has_no_verifiable_setting()
    {
        var states = new (bool Enabled, bool ExplicitValue)[]
        {
            (true, true),
            (true, false),
            (true, true)
        };
        Assert.Equal("Unknown", WindowsCommandExecutor.ClassifyFirewallProfileStates(states));
    }

    [Fact]
    public void Explicit_disabled_firewall_has_priority_over_unobserved_profiles()
    {
        var states = new (bool Enabled, bool ExplicitValue)[]
        {
            (true, false),
            (false, true),
            (true, true)
        };
        Assert.Equal("Disabled", WindowsCommandExecutor.ClassifyFirewallProfileStates(states));
    }

    [Fact]
    public void Firewall_cannot_be_reported_enabled_without_all_three_confirmed_profiles()
    {
        Assert.Equal("Unknown", WindowsCommandExecutor.ClassifyFirewallProfileStates(
            Array.Empty<(bool Enabled, bool ExplicitValue)>()));
        Assert.Equal("Enabled", WindowsCommandExecutor.ClassifyFirewallProfileStates(
            new (bool Enabled, bool ExplicitValue)[]
            {
                (true, true), (true, true), (true, true)
            }));
    }
}
