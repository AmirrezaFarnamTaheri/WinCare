using System.IO.Pipes;
using WinCare.Infrastructure.IPC;

namespace WinCare.Infrastructure.Tests;

public sealed class GuardPipeClientTests
{
    [Fact]
    public async Task SendCommandAsync_PropagatesCancellationWhileWaitingForResponse()
    {
        await using var server = new NamedPipeServerStream(
            "WinCareGuardIPC",
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        await using var client = new GuardPipeClient();

        Task serverConnected = server.WaitForConnectionAsync();
        Assert.True(await client.TryConnectAsync());
        await serverConnected;

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendCommandAsync("status", cancellation.Token));
        Assert.False(client.IsConnected);
    }
}
