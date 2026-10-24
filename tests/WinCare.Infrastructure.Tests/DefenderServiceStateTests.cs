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
}
