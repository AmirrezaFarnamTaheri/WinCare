using WinCare.Infrastructure.Commands;

namespace WinCare.Infrastructure.Tests;

public sealed class BoundedProcessRunnerTests
{
    [Fact]
    public async Task CancelledRequestIsRejectedBeforeResolvingOrStartingExecutable()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new BoundedProcessRunner().RunAsync("missing-wincare-test-executable", [], cancellation.Token));
    }

    [Fact]
    public async Task InvalidTimeoutIsRejectedBeforeStartingExecutable()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new BoundedProcessRunner().RunAsync("missing-wincare-test-executable", [], default,
                timeout: TimeSpan.FromSeconds(-2)));
    }
}
