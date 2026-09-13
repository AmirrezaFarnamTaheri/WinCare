namespace WinCare.Application.Downloads;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public enum DownloadStatus
{
    Pending,
    Downloading,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public sealed record DownloadTaskItem(
    string TaskId,
    string Url,
    string DestinationPath,
    int Priority,
    long TotalBytes,
    long DownloadedBytes,
    DownloadStatus Status,
    string? ErrorMessage);

/// <summary>
/// Prioritized, bounded download scheduler with atomic destination replacement,
/// cancellation, pause/resume, progress tracking, and bounded transient retries.
/// </summary>
public sealed class DownloadTaskScheduler : IDisposable
{
    private const int MaxRetries = 3;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _concurrencyThrottle;
    private readonly ConcurrentDictionary<string, DownloadTaskState> _tasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _ownsHttpClient;
    private int _disposed;

    public sealed class DownloadTaskState
    {
        public required string TaskId { get; init; }
        public required string Url { get; init; }
        public required string DestinationPath { get; init; }
        public required int Priority { get; init; }
        public object SyncRoot { get; } = new();
        public long TotalBytes { get; set; }
        public long DownloadedBytes { get; set; }
        public DownloadStatus Status { get; set; }
        public string? ErrorMessage { get; set; }
        public CancellationTokenSource Cts { get; set; } = new();
        public bool PauseRequested { get; set; }

        public DownloadTaskItem ToSnapshot()
        {
            lock (SyncRoot)
            {
                return new(TaskId, Url, DestinationPath, Priority, TotalBytes, DownloadedBytes, Status, ErrorMessage);
            }
        }
    }

    public DownloadTaskScheduler(int maxConcurrentDownloads = 3, HttpClient? httpClient = null)
    {
        _concurrencyThrottle = new SemaphoreSlim(Math.Max(1, maxConcurrentDownloads));
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    public string EnqueueTask(string url, string destinationPath, int priority = 0)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Download URL must be an absolute HTTP or HTTPS URL.", nameof(url));
        }

        string fullDestination = Path.GetFullPath(destinationPath);
        string taskId = Guid.NewGuid().ToString("N");
        _tasks[taskId] = new DownloadTaskState
        {
            TaskId = taskId,
            Url = uri.AbsoluteUri,
            DestinationPath = fullDestination,
            Priority = priority,
            Status = DownloadStatus.Pending
        };
        return taskId;
    }

    public IReadOnlyList<DownloadTaskItem> GetAllTasks() => _tasks.Values
        .Select(t => t.ToSnapshot())
        .OrderByDescending(t => t.Priority)
        .ThenBy(t => t.TaskId, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public DownloadTaskItem? GetTask(string taskId) =>
        _tasks.TryGetValue(taskId, out var state) ? state.ToSnapshot() : null;

    public bool CancelTask(string taskId)
    {
        if (!_tasks.TryGetValue(taskId, out var state)) return false;
        lock (state.SyncRoot)
        {
            if (state.Status is DownloadStatus.Completed or DownloadStatus.Cancelled) return false;
            state.PauseRequested = false;
            state.Status = DownloadStatus.Cancelled;
            state.ErrorMessage = null;
            state.Cts.Cancel();
            return true;
        }
    }

    public bool PauseTask(string taskId)
    {
        if (!_tasks.TryGetValue(taskId, out var state)) return false;
        lock (state.SyncRoot)
        {
            if (state.Status is not (DownloadStatus.Pending or DownloadStatus.Downloading)) return false;
            state.PauseRequested = true;
            state.Status = DownloadStatus.Paused;
            state.Cts.Cancel();
            return true;
        }
    }

    public bool ResumeTask(string taskId)
    {
        if (!_tasks.TryGetValue(taskId, out var state)) return false;
        lock (state.SyncRoot)
        {
            if (state.Status != DownloadStatus.Paused) return false;
            state.Cts.Dispose();
            state.Cts = new CancellationTokenSource();
            state.PauseRequested = false;
            state.Status = DownloadStatus.Pending;
            state.ErrorMessage = null;
            return true;
        }
    }

    public async Task ProcessPendingTasksAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var candidates = _tasks.Values
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.TaskId, StringComparer.OrdinalIgnoreCase)
            .Where(TryReserve)
            .ToList();

        await Task.WhenAll(candidates.Select(t => ExecuteSingleTaskWithThrottleAsync(t, ct))).ConfigureAwait(false);
    }

    private static bool TryReserve(DownloadTaskState task)
    {
        lock (task.SyncRoot)
        {
            if (task.Status != DownloadStatus.Pending) return false;
            task.Status = DownloadStatus.Downloading;
            task.ErrorMessage = null;
            return true;
        }
    }

    private async Task ExecuteSingleTaskWithThrottleAsync(DownloadTaskState task, CancellationToken callerToken)
    {
        CancellationToken taskToken;
        lock (task.SyncRoot) taskToken = task.Cts.Token;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, taskToken);
        CancellationToken ct = linked.Token;

        try
        {
            await _concurrencyThrottle.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ApplyCancellationState(task, callerToken);
            return;
        }

        try
        {
            string? directory = Path.GetDirectoryName(task.DestinationPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string tempPath = task.DestinationPath + $".{task.TaskId}.part";

            try
            {
                for (int attempt = 1; attempt <= MaxRetries; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        lock (task.SyncRoot)
                        {
                            task.DownloadedBytes = 0;
                            task.TotalBytes = 0;
                        }

                        using var response = await _httpClient.GetAsync(task.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            if (IsTransient(response.StatusCode) && attempt < MaxRetries)
                            {
                                await DelayBeforeRetryAsync(attempt, response, ct).ConfigureAwait(false);
                                continue;
                            }
                            response.EnsureSuccessStatusCode();
                        }

                        long? expectedLength = response.Content.Headers.ContentLength;
                        lock (task.SyncRoot) task.TotalBytes = expectedLength ?? 0;

                        await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                        await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
                        {
                            byte[] buffer = new byte[64 * 1024];
                            int read;
                            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                            {
                                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                                lock (task.SyncRoot) task.DownloadedBytes += read;
                            }
                            await output.FlushAsync(ct).ConfigureAwait(false);
                        }

                        long actualLength;
                        lock (task.SyncRoot) actualLength = task.DownloadedBytes;
                        if (expectedLength.HasValue && actualLength != expectedLength.Value)
                            throw new IOException($"Download ended at {actualLength} bytes; expected {expectedLength.Value} bytes.");

                        File.Move(tempPath, task.DestinationPath, overwrite: true);
                        lock (task.SyncRoot)
                        {
                            task.Status = DownloadStatus.Completed;
                            task.ErrorMessage = null;
                        }
                        return;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        ApplyCancellationState(task, callerToken);
                        return;
                    }
                    catch (Exception ex) when (IsTransient(ex) && attempt < MaxRetries)
                    {
                        TryDelete(tempPath);
                        await Task.Delay(TimeSpan.FromMilliseconds(200 * (1 << (attempt - 1))), ct).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lock (task.SyncRoot)
                {
                    task.Status = DownloadStatus.Failed;
                    task.ErrorMessage = ex.Message;
                }
            }
            finally
            {
                if (task.ToSnapshot().Status != DownloadStatus.Completed) TryDelete(tempPath);
            }
        }
        finally
        {
            _concurrencyThrottle.Release();
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.RequestTimeout || (int)status == 429 || (int)status >= 500;

    private static bool IsTransient(Exception ex) => ex is HttpRequestException or IOException;

    private static async Task DelayBeforeRetryAsync(int attempt, HttpResponseMessage response, CancellationToken ct)
    {
        TimeSpan delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(200 * (1 << (attempt - 1)));
        if (delay > TimeSpan.FromSeconds(10)) delay = TimeSpan.FromSeconds(10);
        await Task.Delay(delay, ct).ConfigureAwait(false);
    }

    private static void ApplyCancellationState(DownloadTaskState task, CancellationToken callerToken)
    {
        lock (task.SyncRoot)
        {
            if (task.PauseRequested)
            {
                task.Status = DownloadStatus.Paused;
            }
            else if (callerToken.IsCancellationRequested && task.Status != DownloadStatus.Cancelled)
            {
                task.Status = DownloadStatus.Pending;
            }
            else
            {
                task.Status = DownloadStatus.Cancelled;
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var task in _tasks.Values)
        {
            lock (task.SyncRoot)
            {
                task.Cts.Cancel();
                task.Cts.Dispose();
            }
        }
        _concurrencyThrottle.Dispose();
        if (_ownsHttpClient) _httpClient.Dispose();
    }
}
