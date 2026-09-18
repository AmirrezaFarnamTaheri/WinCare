using System.Net;
using System.Reflection;
using WinCare.Infrastructure.Commands;
using Xunit;

namespace WinCare.Infrastructure.Tests;

public sealed class HttpClientOwnershipTests
{
    [Fact]
    public async Task Disposing_executor_leaves_injected_client_usable_and_caller_owned()
    {
        var handler = new RecordingHttpMessageHandler();
        using var client = new HttpClient(handler);
        using var executor = new WindowsCommandExecutor(
            state: new CommandStateStore(Path.GetTempPath()), httpClient: client);

        executor.Dispose();
        executor.Dispose();

        Assert.Equal(0, handler.DisposeCount);
        // The recording handler terminates the request in memory; no socket is opened.
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://ownership.invalid/");
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, handler.SendCount);

        client.Dispose();
        Assert.Equal(1, handler.DisposeCount);
    }

    [Fact]
    public void Disposing_executor_disposes_its_internally_created_client()
    {
        using var executor = new WindowsCommandExecutor(
            state: new CommandStateStore(Path.GetTempPath()));
        FieldInfo? field = typeof(WindowsCommandExecutor).GetField(
            "_httpClient", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var client = Assert.IsType<HttpClient>(field.GetValue(executor));

        executor.Dispose();
        executor.Dispose();

        // Setting Timeout checks disposal without sending any network request.
        Assert.Throws<ObjectDisposedException>(() => client.Timeout = TimeSpan.FromSeconds(1));
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }
        public int DisposeCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }
            base.Dispose(disposing);
        }
    }
}
