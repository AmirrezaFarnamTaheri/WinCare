using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Downloads;

namespace WinCare.Application.Tests;

/// <summary>
/// Regression tests for the download scheduler's pause/resume race. A paused attempt observes its
/// cancellation token some time after the pause; if the task is resumed in that window, the stale
/// attempt must not overwrite the resumed state or touch the destination file.
/// </summary>
public sealed class DownloadSchedulerPauseResumeTests
{
    /// <summary>
    /// An HTTP handler that blocks each request until the caller releases it, so the test can put
    /// an attempt in flight, pause and resume around it, and then let it complete.
    /// </summary>
    private sealed class GatedHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<bool> _release = new();
        private readonly string _body;
        public bool RequestStarted { get; private set; }

        public GatedHandler(string body) => _body = body;

        public void ReleaseResponse() => _release.TrySetResult(true);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestStarted = true;
            await _release.Task;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/octet-stream")
            };
        }
    }

    private static string TempDestination()
        => Path.Combine(Path.GetTempPath(), "WinCarePauseRace_" + Guid.NewGuid().ToString("N") + ".bin");

    [Fact]
    public async Task ResumedTask_CompletesAndStaleAttempt_DoesNotClobberState()
    {
        var handler = new GatedHandler("resumed-payload");
        using var scheduler = new DownloadTaskScheduler(maxConcurrentDownloads: 1, httpClient: new HttpClient(handler));
        string destination = TempDestination();

        string taskId = scheduler.EnqueueTask("https://downloads.example.org/payload.bin", destination);
        Task processing = scheduler.ProcessPendingTasksAsync();

        // Wait until the attempt is in flight, then pause and immediately resume it. The attempt
        // for the paused epoch must observe cancellation and retire without touching the resumed
        // attempt's state or destination.
        for (int i = 0; i < 50 && !handler.RequestStarted; i++) await Task.Delay(20);
        Assert.True(handler.RequestStarted, "The download attempt should have started.");

        Assert.True(scheduler.PauseTask(taskId));
        Assert.True(scheduler.ResumeTask(taskId));
        Assert.Equal(DownloadStatus.Pending, scheduler.GetTask(taskId)!.Status);

        // Releasing the (now stale) first response lets that attempt run to completion against the
        // paused epoch. The resumed attempt must still be the one that publishes the destination.
        handler.ReleaseResponse();
        await processing;

        // The paused epoch's response was released and retired; the resumed attempt processes next.
        await scheduler.ProcessPendingTasksAsync();
        var final = scheduler.GetTask(taskId)!;
        Assert.Equal(DownloadStatus.Completed, final.Status);
        Assert.Equal("resumed-payload", await File.ReadAllTextAsync(destination));

        TryCleanup(destination);
    }

    [Fact]
    public void Pause_And_Cancel_BumpEpoch_SoStaleAttemptsRetire()
    {
        using var scheduler = new DownloadTaskScheduler(maxConcurrentDownloads: 1);
        string destination = TempDestination();
        string taskId = scheduler.EnqueueTask("https://downloads.example.org/payload.bin", destination);

        // The state machine must reject transitions that would leave a stale attempt as the current
        // owner of the task.
        Assert.True(scheduler.CancelTask(taskId));
        Assert.Equal(DownloadStatus.Cancelled, scheduler.GetTask(taskId)!.Status);
        Assert.False(scheduler.PauseTask(taskId), "A cancelled task cannot be paused.");
        Assert.False(scheduler.ResumeTask(taskId), "A cancelled task cannot be resumed.");

        TryCleanup(destination);
    }

    private static void TryCleanup(string destination)
    {
        // Remove the whole dedicated directory; it holds only this test's destination and the
        // epoch-scoped .part files the scheduler wrote next to it.
        try { if (Directory.Exists(Path.GetDirectoryName(destination))) Directory.Delete(Path.GetDirectoryName(destination)!, recursive: true); } catch { }
    }
}
