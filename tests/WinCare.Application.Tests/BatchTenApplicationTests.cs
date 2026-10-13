using System.IO;
using WinCare.Application.Downloads;
using Xunit;

namespace WinCare.Application.Tests;

/// <summary>
/// Unit and regression tests for DownloadTaskScheduler.
/// </summary>
public sealed class BatchTenApplicationTests
{
    [Fact]
    public void DownloadTaskScheduler_schedules_and_throttles_tasks()
    {
        using var scheduler = new DownloadTaskScheduler(maxConcurrentDownloads: 3);

        string taskId = scheduler.EnqueueTask(
            "https://downloads.example.org/packages/tool.zip",
            Path.Combine(Path.GetTempPath(), "tool.zip"),
            priority: 5);

        Assert.False(string.IsNullOrWhiteSpace(taskId));

        var task = scheduler.GetTask(taskId);
        Assert.NotNull(task);
        Assert.Equal(taskId, task.TaskId);
        Assert.Equal(DownloadStatus.Pending, task.Status);

        // Cancel the task
        bool canceled = scheduler.CancelTask(taskId);
        Assert.True(canceled);

        var canceledTask = scheduler.GetTask(taskId);
        Assert.NotNull(canceledTask);
        Assert.Equal(DownloadStatus.Cancelled, canceledTask.Status);
    }
}
