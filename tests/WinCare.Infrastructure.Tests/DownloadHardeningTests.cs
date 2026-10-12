using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Commands;
using WinCare.Infrastructure.Downloads;

namespace WinCare.Infrastructure.Tests;

public sealed class DownloadHardeningTests
{
    private static Task<CommandHandlerOutcome> Run(WindowsCommandExecutor executor, string command, object parameters) =>
        executor.ExecuteAsync(WinCare.CommandCatalog.CommandCatalog.Find(command)!,
            CommandRequest.Execute(command, JsonSerializer.SerializeToElement(parameters)), CancellationToken.None);
    private static string Root()
    {
        string root = Path.Combine(Path.GetTempPath(), "WinCareHardening_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public async Task Signed_link_survives_restart_without_plaintext_or_public_ciphertext()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Root();
        try
        {
            const string url = "https://example.com/private-path/payload.bin?signature=TOP_SECRET#PRIVATE_FRAGMENT";
            string destination = Path.Combine(root, "payload.bin");
            string id;
            using (var creator = new WindowsCommandExecutor(root))
            {
                var created = await Run(creator, "download-create", new { Url = url, Destination = destination });
                Assert.Equal(CommandResultStatus.Succeeded, created.Status);
                id = created.Data!.Value.GetProperty("id").GetString()!;
                Assert.DoesNotContain("protectedUrl", created.Data.Value.GetRawText());
                Assert.DoesNotContain("TOP_SECRET", created.Data.Value.GetRawText());
            }
            string persisted = await File.ReadAllTextAsync(Path.Combine(root, "downloads.json"));
            Assert.DoesNotContain("TOP_SECRET", persisted);
            Assert.DoesNotContain("private-path", persisted);
            Assert.DoesNotContain("PRIVATE_FRAGMENT", persisted);
            Assert.Contains("protectedUrl", persisted);
            using var http = new HttpClient(new Handler(request =>
            {
                Assert.Equal(url, request.RequestUri!.AbsoluteUri);
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
            }));
            using var restarted = new WindowsCommandExecutor(null, null, new CommandStateStore(root), http);
            Assert.Equal(CommandResultStatus.Succeeded, (await Run(restarted, "download-start", new { Id = id })).Status);
            Assert.DoesNotContain("protectedUrl", await File.ReadAllTextAsync(Path.Combine(root, "downloads.json")));
            string origin = await File.ReadAllTextAsync(destination + ":Zone.Identifier");
            Assert.Contains("ZoneId=3", origin);
            Assert.DoesNotContain("https://", origin);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Legacy_queue_migrates_before_read_returns_and_retains_exact_transfer_link()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Root();
        try
        {
            const string url = "https://example.com/a?opaque=SECRET#FRAGMENT";
            await File.WriteAllTextAsync(Path.Combine(root, "downloads.json"), JsonSerializer.Serialize(new[] { new { id = "old", url, state = "Suspended" } }));
            var records = await new CommandStateStore(root).ReadArrayAsync("downloads", CancellationToken.None);
            var job = Assert.Single(records.EnumerateArray());
            Assert.Equal(url, DownloadProtection.TransferUri(job).AbsoluteUri);
            Assert.DoesNotContain("SECRET", await File.ReadAllTextAsync(Path.Combine(root, "downloads.json")));
            Assert.DoesNotContain("protectedUrl", DownloadProtection.PublicRecords(records).GetRawText());
            string encrypted = job.GetProperty("protectedUrl").GetString()!;
            byte[] corrupt = Convert.FromBase64String(encrypted); corrupt[^1] ^= 1;
            Assert.Throws<CryptographicException>(() => DownloadProtection.Unprotect(Convert.ToBase64String(corrupt)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Concurrent_creators_reserve_exactly_one_explicit_destination_across_roots()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Root();
        try
        {
            using var first = new WindowsCommandExecutor(Path.Combine(root, "first"));
            using var second = new WindowsCommandExecutor(Path.Combine(root, "second"));
            var args = new { Url = "https://example.com/file", Destination = Path.Combine(root, "file.bin") };
            var outcomes = await Task.WhenAll(Run(first, "download-create", args), Run(second, "download-create", args));
            Assert.Single(outcomes, result => result.Status == CommandResultStatus.Succeeded);
            Assert.Single(outcomes, result => result.Status == CommandResultStatus.Blocked);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Independent_worker_blocks_start_reconcile_cancel_remove_and_crash_releases_handle()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Root();
        Process? child = null;
        try
        {
            string destination = Path.Combine(root, "file.bin");
            using var executor = new WindowsCommandExecutor(root);
            var created = await Run(executor, "download-create", new { Url = "https://example.com/file", Destination = destination });
            string id = created.Data!.Value.GetProperty("id").GetString()!;
            await File.WriteAllBytesAsync(destination + ".wincare.download", [1, 2, 3]);
            string ready = Path.Combine(root, "ready");
            string script = "$h=[IO.File]::Open('" + (destination + ".wincare.lock").Replace("'", "''") +
                "',[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);[IO.File]::WriteAllText('" + ready.Replace("'", "''") +
                "','ready');Start-Sleep -Seconds 120";
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-EncodedCommand");
            start.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)));
            child = Process.Start(start)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!File.Exists(ready)) { timeout.Token.ThrowIfCancellationRequested(); await Task.Delay(25, timeout.Token); }
            foreach (string command in new[] { "download-start", "download-reconcile", "download-cancel", "download-remove" })
                Assert.Equal(CommandResultStatus.Blocked, (await Run(executor, command, new { Id = id })).Status);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(destination + ".wincare.download"));
            child.Kill(); await child.WaitForExitAsync();
            Assert.Equal(CommandResultStatus.Succeeded, (await Run(executor, "download-cancel", new { Id = id })).Status);
            Assert.False(File.Exists(destination + ".wincare.download"));
        }
        finally
        {
            if (child is not null) { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } child.Dispose(); }
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Interrupted_publication_reconciles_digest_and_origin_without_source_checksum()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Root();
        try
        {
            string destination = Path.Combine(root, "published.bin");
            byte[] bytes = [4, 5, 6];
            string digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var store = new CommandStateStore(root);
            await store.WriteAsync("downloads", JsonSerializer.SerializeToElement(new[] {
                new { id = "interrupted", url = "https://example.com/file", state = "Publishing", destination, sha256Actual = digest, sha256 = "" }
            }), CancellationToken.None);
            await File.WriteAllBytesAsync(destination, bytes);
            using var executor = new WindowsCommandExecutor(null, null, store);
            Assert.Equal(CommandResultStatus.Succeeded, (await Run(executor, "download-reconcile", new { Id = "interrupted" })).Status);
            Assert.Contains("ZoneId=3", await File.ReadAllTextAsync(destination + ":Zone.Identifier"));
            Assert.Equal("Completed", Assert.Single((await store.ReadArrayAsync("downloads", CancellationToken.None)).EnumerateArray()).GetProperty("state").GetString());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Http_failure_does_not_disclose_signed_request_in_outcome()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Root();
        try
        {
            using var http = new HttpClient(new Handler(_ => throw new HttpRequestException("https://example.com/?signature=SECRET")));
            using var executor = new WindowsCommandExecutor(null, null, new CommandStateStore(root), http);
            var created = await Run(executor, "download-create", new { Url = "https://example.com/?signature=SECRET", Destination = Path.Combine(root, "file.bin") });
            var failed = await Run(executor, "download-start", new { Id = created.Data!.Value.GetProperty("id").GetString()! });
            Assert.Equal(CommandResultStatus.Failed, failed.Status);
            Assert.DoesNotContain("SECRET", failed.Message);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
}
