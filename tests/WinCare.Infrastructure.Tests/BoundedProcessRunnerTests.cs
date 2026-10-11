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
    public async Task WindowsCancellationReapsStartedChildBeforeReturning()
    {
        if (!OperatingSystem.IsWindows()) return;
        string ping = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ping.exe");
        if (!File.Exists(ping)) return;
        using CancellationTokenSource cancel = new(TimeSpan.FromMilliseconds(600));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new BoundedProcessRunner().RunAsync(ping, ["-n", "30", "127.0.0.1"], cancel.Token));
    }

    [Fact]
    public async Task InvalidTimeoutIsRejectedBeforeStartingExecutable()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new BoundedProcessRunner().RunAsync("missing-wincare-test-executable", [], default,
                timeout: TimeSpan.FromSeconds(-2)));
    }
}
