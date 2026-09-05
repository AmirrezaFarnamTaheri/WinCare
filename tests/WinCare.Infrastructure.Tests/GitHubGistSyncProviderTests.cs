using System.Net;
using WinCare.Infrastructure.Sync;

namespace WinCare.Infrastructure.Tests;

public sealed class GitHubGistSyncProviderTests
{
    [Theory]
    [InlineData("../user")]
    [InlineData("abc?query=1")]
    [InlineData("https://example.com")]
    public async Task Invalid_id_is_rejected_before_network_access(string id)
    {
        var handler = new ResponseHandler("{}");
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => new GitHubGistSyncProvider(client)
            .DownloadProfileAsync(id, "test passphrase", ""));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Truncated_remote_file_is_rejected_before_decryption()
    {
        using var client = new HttpClient(new ResponseHandler("""
            {"files":{"wincare-profile.enc":{"content":"incomplete", "truncated":true}}}
            """));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new GitHubGistSyncProvider(client)
            .DownloadProfileAsync("abcdef", "test passphrase", ""));
        Assert.Contains("truncated", error.Message);
    }

    [Fact]
    public async Task Oversized_response_is_rejected()
    {
        using var client = new HttpClient(new ResponseHandler(new string('x', 2 * 1024 * 1024 + 1)));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new GitHubGistSyncProvider(client)
            .DownloadProfileAsync("abcdef", "test passphrase", ""));
        Assert.Contains("size limit", error.Message);
    }

    private sealed class ResponseHandler(string body) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
