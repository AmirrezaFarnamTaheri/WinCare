using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Commands;
using Xunit;

namespace WinCare.Infrastructure.Tests;

public sealed class NativeDownloadWorkspaceTests
{
    [Fact]
    public async Task Explicit_existing_destination_is_blocked_without_modifying_the_file()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = TemporaryRoot();
        try
        {
            string destination = Path.Combine(root, "existing.zip");
            await File.WriteAllTextAsync(destination, "untouched");
            using WindowsCommandExecutor executor = new(root);
            var request = CommandRequest.Execute("download-create", JsonSerializer.SerializeToElement(
                new { Url = "https://example.com/existing.zip", Destination = destination }));
            var outcome = await executor.ExecuteAsync(
                WinCare.CommandCatalog.CommandCatalog.Find("download-create")!, request, CancellationToken.None);

            Assert.Equal(CommandResultStatus.Blocked, outcome.Status);
            Assert.Equal("untouched", await File.ReadAllTextAsync(destination));
            Assert.False(File.Exists(destination + ".wincare.download"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Successful_http_transfer_publishes_verified_bytes_and_removes_partial()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = TemporaryRoot();
        try
        {
            string destination = Path.Combine(root, "complete.bin");
            byte[] bytes = Enumerable.Range(0, 8192).Select(i => (byte)(i % 251)).ToArray();
            using var http = new HttpClient(new SuccessfulTransferHandler(bytes));
            using WindowsCommandExecutor executor = new(null, null, new CommandStateStore(root), http);
            var created = await executor.ExecuteAsync(
                WinCare.CommandCatalog.CommandCatalog.Find("download-create")!,
                CommandRequest.Execute("download-create", JsonSerializer.SerializeToElement(
                    new { Url = "https://example.com/complete.bin", Destination = destination })),
                CancellationToken.None);
            Assert.Equal(CommandResultStatus.Succeeded, created.Status);
            string id = created.Data!.Value.GetProperty("id").GetString()!;

            var result = await executor.ExecuteAsync(
                WinCare.CommandCatalog.CommandCatalog.Find("download-start")!,
                CommandRequest.Execute("download-start", JsonSerializer.SerializeToElement(new { Id = id })),
                CancellationToken.None);

            Assert.Equal(CommandResultStatus.Succeeded, result.Status);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
            Assert.False(File.Exists(destination + ".wincare.download"));
            var state = await new CommandStateStore(root).ReadArrayAsync("downloads", CancellationToken.None);
            Assert.Equal("Completed", Assert.Single(state.EnumerateArray()).GetProperty("state").GetString());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Incorrect_http_resume_range_never_appends_to_existing_partial()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = TemporaryRoot();
        try
        {
            string destination = Path.Combine(root, "payload.bin");
            using var http = new HttpClient(new InvalidRangeHandler());
            using WindowsCommandExecutor executor = new(null, null, new CommandStateStore(root), http);
            var created = await executor.ExecuteAsync(
                WinCare.CommandCatalog.CommandCatalog.Find("download-create")!,
                CommandRequest.Execute("download-create", JsonSerializer.SerializeToElement(
                    new { Url = "https://example.com/payload.bin", Destination = destination })),
                CancellationToken.None);
            Assert.Equal(CommandResultStatus.Succeeded, created.Status);
            string id = created.Data!.Value.GetProperty("id").GetString()!;
            byte[] partialBytes = [11, 12, 13, 14];
            await File.WriteAllBytesAsync(destination + ".wincare.download", partialBytes);

            var result = await executor.ExecuteAsync(
                WinCare.CommandCatalog.CommandCatalog.Find("download-start")!,
                CommandRequest.Execute("download-start", JsonSerializer.SerializeToElement(new { Id = id })),
                CancellationToken.None);

            Assert.Equal(CommandResultStatus.Failed, result.Status);
            Assert.Equal(partialBytes, await File.ReadAllBytesAsync(destination + ".wincare.download"));
            Assert.False(File.Exists(destination));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string TemporaryRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "WinCareDownloads_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class SuccessfulTransferHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            });
    }

    private sealed class InvalidRangeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.NotNull(request.Headers.Range);
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent([0, 1, 2, 3]),
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 3, 4);
            return Task.FromResult(response);
        }
    }
}
