using WinCare.Infrastructure.Native;

namespace WinCare.Infrastructure.Tests;

public sealed class MemoryGovernorOutcomeTests
{
    [Fact]
    public void Native_failures_cannot_be_reported_as_successful_memory_compaction()
    {
        var result = new MemoryGovernor.MemoryCompactionResult(
            StandbyRequestSucceeded: false,
            WorkingSetTrimCount: 0,
            WorkingSetAttemptCount: 8,
            ObservedAvailableBytesChange: 500 * 1024 * 1024);

        Assert.False(result.AnySucceeded);
        Assert.Equal(500 * 1024 * 1024, result.ObservedAvailableBytesChange);
    }

    [Fact]
    public void Verified_working_set_request_can_succeed_without_inventing_reclaimed_bytes()
    {
        var result = new MemoryGovernor.MemoryCompactionResult(
            StandbyRequestSucceeded: false,
            WorkingSetTrimCount: 1,
            WorkingSetAttemptCount: 2,
            ObservedAvailableBytesChange: -10);

        Assert.True(result.AnySucceeded);
        Assert.True(result.ObservedAvailableBytesChange < 0);
    }
}
