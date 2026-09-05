using System.IO.Pipes;
using WinCare.Infrastructure.IPC;

namespace WinCare.Infrastructure.Tests;

public sealed class GuardPipeClientTests
{
    [Fact]
    public async Task SendCommandAsync_TimesOutAnUnresponsivePeerWithoutCallerDeadline()
    {
        await using var server = new NamedPipeServerStream("WinCareGuardIPC", PipeDirection.InOut,
            1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new GuardPipeClient();
        var connected = server.WaitForConnectionAsync();
        Assert.True(await client.TryConnectAsync());
        await connected;
        Assert.Null(await client.SendCommandAsync("ping").WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task SendCommandAsync_ReconnectsForEachConcurrentRequest()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = new NamedPipeServerStream("WinCareGuardIPC", PipeDirection.InOut,
            1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serve = Task.Run(async () =>
        {
            for (int i = 0; i < 2; i++)
            {
                await server.WaitForConnectionAsync(deadline.Token);
                using var reader = new StreamReader(server, leaveOpen: true);
                Assert.Equal("ping", await reader.ReadLineAsync(deadline.Token));
                await server.WriteAsync("pong\n"u8.ToArray(), deadline.Token);
                // Wait for the client to consume the response and close its one-shot connection.
                Assert.Equal(0, await server.ReadAsync(new byte[1], deadline.Token));
                server.Disconnect();
            }
        }, deadline.Token);
        await using var client = new GuardPipeClient();
        var responses = await Task.WhenAll(client.SendCommandAsync("ping", deadline.Token),
            client.SendCommandAsync("ping", deadline.Token));
        Assert.All(responses, response => Assert.Equal("pong", response));
        Assert.False(client.IsConnected);
        await serve;
    }

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
