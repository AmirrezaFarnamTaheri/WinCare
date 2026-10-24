using System.Text.Json;
using WinCare.Application.Downloads;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Tests;

public sealed class DownloadCenterTests
{
    [Fact]
    public void Projection_redacts_secret_bearing_links_and_calculates_bounded_progress()
    {
        using var document = JsonDocument.Parse("""
            [
              {"id":"a1","url":"https://alice:password@example.com/big.zip?token=supersecret",
               "destination":"C:\\Users\\Alice\\Downloads\\big.zip","state":"Running",
               "partialBytes":4194304,"expectedBytes":8388608,"createdAt":"2026-10-11T11:00:00Z"},
              {"id":"a2","url":"https://files.example.com/small.zip?key=never-show",
               "destination":"small.zip","state":"Completed",
               "bytes":1048576,"createdAt":"2026-10-11T10:00:00Z"},
              {"id":"no-id","state":"Pending"}, 3
            ]
            """);
        var items = DownloadJobProjection.Project(document.RootElement);
        Assert.Equal(3, items.Count);
        var running = Assert.Single(items, row => row.Id == "a1");
        Assert.Equal("big.zip", running.Name);
        Assert.Equal("example.com", running.Host);
        Assert.Equal(50d, running.ProgressPercent);
        Assert.True(running.CanPause);
        Assert.False(running.IsIndeterminate);
        Assert.DoesNotContain("password", running.AccessibleName);
        Assert.DoesNotContain("supersecret", running.AccessibleName);
        Assert.All(items, row => Assert.DoesNotContain("never-show", row.AccessibleName));
    }

    [Fact]
    public void Projection_recovers_interrupted_jobs_without_inventing_live_workers()
    {
        using var data = JsonDocument.Parse("""
            [{"id":"stale","state":"Running","partialBytes":4096,
              "destination":"archive.bin","url":"https://example.com/archive.bin"}]
            """);
        var paused = Assert.Single(DownloadJobProjection.Project(data.RootElement, []));
        Assert.Equal("Interrupted", paused.State);
        Assert.True(paused.CanResume);
        Assert.False(paused.CanPause);
        var active = Assert.Single(DownloadJobProjection.Project(data.RootElement, ["stale"]));
        Assert.Equal("Running", active.State);
        Assert.True(active.CanPause);
    }

    [Fact]
    public void Projection_extracts_Windows_file_name_independent_of_test_platform()
    {
        using var data = JsonDocument.Parse("""
            [{"id":"portable","state":"Completed","bytes":1234,
              "destination":"C:\\\\Users\\\\Example\\\\Downloads\\\\movie.mp4",
              "url":"https://example.com/movie.mp4"}]
            """);
        DownloadJobRow row = Assert.Single(DownloadJobProjection.Project(data.RootElement));
        Assert.Equal("movie.mp4", row.Name);
        Assert.DoesNotContain("Users", row.AccessibleName);
    }

    [Fact]
    public void Projection_does_not_invent_a_percentage_when_total_is_unknown()
    {
        using var document = JsonDocument.Parse("""
            [{"id":"unknown","state":"Running","partialBytes":2000,
              "destination":"archive.bin","url":"https://example.com/archive.bin"}]
            """);
        DownloadJobRow job = Assert.Single(DownloadJobProjection.Project(document.RootElement));
        Assert.True(job.IsIndeterminate);
        Assert.True(job.ShowProgress);
        Assert.Equal("2 KB", job.TransferLabel);
    }

    [Fact]
    public void Projection_skips_invalid_entries_and_bounds_collection_size()
    {
        Assert.Empty(DownloadJobProjection.Project(null));
        using var bad = JsonDocument.Parse("{}");
        Assert.Empty(DownloadJobProjection.Project(bad.RootElement));
        using var data = JsonDocument.Parse(
            "[" + string.Join(",", Enumerable.Range(0, 300)
                .Select(i => $$"""{"id":"{{i}}","state":"Pending","destination":"file.txt"}""")) + "]");
        Assert.Equal(250, DownloadJobProjection.Project(data.RootElement).Count);
    }

    [Fact]
    public async Task Queue_facade_reserves_one_worker_per_id_and_uses_canonical_dispatcher()
    {
        var dispatcher = new RecordingDispatcher();
        var manager = new DownloadCenterService(dispatcher);
        Assert.True(manager.TryStart("one", resume: false, out Task<CommandResult> first));
        await dispatcher.TransferStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(manager.TryStart("one", resume: true, out _));
        dispatcher.ReleaseTransfer();
        Assert.Equal("download-start", (await first).CommandId);
        // Completion is observable, and the reservation is removed afterwards.
        await Task.Yield();
        Assert.Equal("download-create", (await manager.AddAsync("https://example.com/f.zip")).CommandId);
        await Assert.ThrowsAsync<ArgumentException>(() => manager.AddAsync("file:///etc/passwd"));
        await Assert.ThrowsAsync<ArgumentException>(() => manager.AddAsync("https://user:password@example.com/file.zip"));
    }

    [Fact]
    public async Task Optional_SHA256_is_normalized_and_sent_to_the_native_download_queue()
    {
        var dispatcher = new RecordingDispatcher();
        var manager = new DownloadCenterService(dispatcher);
        const string checksum = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";

        await manager.AddAsync("https://example.com/archive.zip", checksum);

        Assert.Equal(checksum.ToLowerInvariant(), dispatcher.LastCreateParameters?.GetProperty("Sha256").GetString());
    }

    [Theory]
    [InlineData("short")]
    [InlineData("ZBCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    public async Task Malformed_SHA256_is_rejected_before_queue_mutation(string checksum)
    {
        var dispatcher = new RecordingDispatcher();
        var manager = new DownloadCenterService(dispatcher);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.AddAsync("https://example.com/file.zip", checksum));

        Assert.Null(dispatcher.LastCreateParameters);
    }

    [Fact]
    public void Download_intake_requires_a_full_checksum_only_when_one_is_specified()
    {
        var vm = new WinCare.App.ViewModels.Pages.DownloadsPageViewModel(
            new DownloadCenterService(new RecordingDispatcher()));
        vm.NewUrl = "https://example.com/archive.zip";
        Assert.True(vm.CanAdd);

        vm.ExpectedSha256 = "almost-a-hash";
        Assert.False(vm.CanAdd);
        vm.ExpectedSha256 = new string('F', 64);
        Assert.True(vm.CanAdd);
        vm.ExpectedSha256 = " ";
        Assert.True(vm.CanAdd);
    }

    private sealed class RecordingDispatcher : ICommandDispatcher
    {
        private readonly TaskCompletionSource<bool> _releaseTransfer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> TransferStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public JsonElement? LastCreateParameters { get; private set; }
        public void ReleaseTransfer() => _releaseTransfer.TrySetResult(true);
        public async Task<CommandResult> ExecuteAsync(CommandRequest request, CommandExecutionOptions options, CancellationToken token)
        {
            if (request.CommandId == "download-create")
                LastCreateParameters = request.Parameters.Clone();
            if (request.CommandId is "download-start" or "download-resume")
            {
                TransferStarted.TrySetResult(true);
                await _releaseTransfer.Task.WaitAsync(token);
            }
            return new CommandResult(request.CommandId, request.CorrelationId,
                CommandResultStatus.Succeeded, "test.ok", "Completed", null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false);
        }

        public bool RegisterDynamicCommand(CommandDefinition definition, ICommandHandler handler) => false;
        public bool UnregisterDynamicCommand(string commandId) => false;
    }
}
