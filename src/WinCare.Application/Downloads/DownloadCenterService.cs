using System.Collections.Concurrent;
using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;

namespace WinCare.Application.Downloads;

/// <summary>
/// Process-lifetime download queue facade. The existing native command dispatcher owns
/// every read and mutation; the UI never spawns an independent downloader.
/// </summary>
public sealed class DownloadCenterService
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly ConcurrentDictionary<string, Task<CommandResult>> _active = new(StringComparer.OrdinalIgnoreCase);

    public DownloadCenterService(ICommandDispatcher dispatcher) =>
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public IReadOnlyCollection<string> ActiveIds => _active.Keys.ToArray();

    public Task<CommandResult> ReadAsync(CancellationToken cancellationToken = default) =>
        _dispatcher.ExecuteAsync(CommandRequest.Preview("downloads"), CommandExecutionOptions.Default, cancellationToken);

    public Task<CommandResult> AddAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ||
            parsed.Scheme is not ("http" or "https"))
            throw new ArgumentException("Enter an HTTP or HTTPS download link.", nameof(url));

        // A user clicking Add explicitly authorizes writing a local queue record, not
        // fetching bytes. The existing dispatcher enforces the catalog admission policy.
        return DispatchAsync("download-create", new { Url = parsed.AbsoluteUri }, cancellationToken);
    }

    public bool TryStart(string id, bool resume, out Task<CommandResult> completion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        // The reservation must precede Task.Run: concurrent start buttons and pages
        // cannot launch two workers for the same native job ID.
        var promise = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        completion = promise.Task;
        if (!_active.TryAdd(id, completion)) return false;
        _ = RunTransferAsync(id, resume, promise);
        return true;
    }

    private async Task RunTransferAsync(string id, bool resume, TaskCompletionSource<CommandResult> promise)
    {
        try
        {
            CommandResult result = await DispatchAsync(
                resume ? "download-resume" : "download-start", new { Id = id }, CancellationToken.None)
                .ConfigureAwait(false);
            promise.TrySetResult(result);
        }
        catch (Exception ex)
        {
            promise.TrySetException(ex);
        }
        finally
        {
            _active.TryRemove(new KeyValuePair<string, Task<CommandResult>>(id, promise.Task));
        }
    }

    public Task<CommandResult> PauseAsync(string id, CancellationToken token = default) =>
        DispatchAsync("download-suspend", new { Id = id }, token);

    public Task<CommandResult> CancelAsync(string id, CancellationToken token = default) =>
        DispatchAsync("download-cancel", new { Id = id }, token);

    public Task<CommandResult> RemoveAsync(string id, CancellationToken token = default) =>
        DispatchAsync("download-remove", new { Id = id }, token);

    public async Task PauseActiveForShutdownAsync(CancellationToken token)
    {
        foreach (string id in _active.Keys)
        {
            token.ThrowIfCancellationRequested();
            // Pausing preserves partial bytes. App shutdown must not discard work.
            await PauseAsync(id, token).ConfigureAwait(false);
        }
        Task<CommandResult>[] operations = _active.Values.ToArray();
        if (operations.Length > 0)
        {
            try { await Task.WhenAll(operations).WaitAsync(token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The launcher is closing. The native journal and persisted download
                // records remain authoritative if an operation faulted during shutdown.
                System.Diagnostics.Debug.WriteLine($"[DownloadCenter] Shutdown worker fault: {ex.GetType().Name}");
            }
        }
    }

    private Task<CommandResult> DispatchAsync(string commandId, object parameters, CancellationToken token)
    {
        var data = JsonSerializer.SerializeToElement(parameters);
        return _dispatcher.ExecuteAsync(
            CommandRequest.Execute(commandId, data),
            new CommandExecutionOptions(ReviewApproved: true),
            token);
    }
}
