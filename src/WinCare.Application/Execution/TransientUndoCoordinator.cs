namespace WinCare.Application.Execution;

using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;
using WinCare.Domain.Compensators;

/// <summary>
/// Notification view model representing an active transient undo window.
/// </summary>
public sealed class UndoNotification
{
    public string SnapshotId { get; }
    public string CommandId { get; }
    public string Description { get; }
    public int DurationSeconds { get; }
    public Func<Task> OnUndo { get; }
    public Action OnDismiss { get; }

    public int RemainingSeconds { get; set; }
    public double ProgressPercentage { get; set; } = 100.0;

    public UndoNotification(
        string snapshotId,
        string commandId,
        string description,
        int durationSeconds,
        Func<Task> onUndo,
        Action onDismiss)
    {
        SnapshotId = snapshotId;
        CommandId = commandId;
        Description = description;
        DurationSeconds = durationSeconds;
        RemainingSeconds = durationSeconds;
        OnUndo = onUndo;
        OnDismiss = onDismiss;
    }

    public async Task TriggerUndoAsync() => await OnUndo();
    public void Dismiss() => OnDismiss();
}

/// <summary>
/// Coordinator managing dispatcher-admitted low-risk command execution and transient undo windows.
/// The caller must capture a fresh reverse-state snapshot and register its compensator before execution.
/// </summary>
public sealed class TransientUndoCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan MaximumSnapshotAge = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeTimers = new();
    private readonly ConcurrentDictionary<string, ICompensatorDefinition> _compensators = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _rollbackClaims = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _usedSnapshotIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, StateSnapshot> _undoableSnapshots = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public TransientUndoCoordinator(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    public UndoNotification? CurrentNotification { get; private set; }

    public event EventHandler<UndoNotification?>? NotificationChanged;

    /// <summary>
    /// Registers a compensator for state rollback operations.
    /// </summary>
    public void RegisterCompensator(ICompensatorDefinition compensator)
    {
        ArgumentNullException.ThrowIfNull(compensator);
        ArgumentException.ThrowIfNullOrWhiteSpace(compensator.CompensatorId);
        if (!_compensators.TryAdd(compensator.CompensatorId, compensator))
        {
            throw new InvalidOperationException($"A compensator named '{compensator.CompensatorId}' is already registered.");
        }
    }

    /// <summary>
    /// Executes an operation optimistically, captures a rollback snapshot, and presents a transient undo toast.
    /// </summary>
    public async Task<CommandHandlerOutcome> ExecuteOptimisticAsync(
        ICommandDispatcher dispatcher,
        CommandDefinition definition,
        CommandRequest request,
        string humanDescription,
        StateSnapshot? preCapturedSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        if (definition is null || request is null ||
            !string.Equals(definition.Id, request.CommandId, StringComparison.OrdinalIgnoreCase))
        {
            return CommandHandlerOutcome.Blocked(
                "execution.definition_required",
                "Optimistic execution requires a matching catalog definition and command request.");
        }

        if (!request.Apply || definition.ReadOnly || definition.Risk is not CommandRisk.Low)
        {
            return CommandHandlerOutcome.Blocked(
                "execution.review_required",
                $"Command '{request.CommandId}' is not eligible for optimistic execution and must use the command review flow.");
        }

        TimeSpan snapshotAge = preCapturedSnapshot is null
            ? TimeSpan.MaxValue
            : _timeProvider.GetUtcNow().UtcDateTime - preCapturedSnapshot.CreatedUtc;
        if (preCapturedSnapshot is null ||
            !string.Equals(preCapturedSnapshot.CommandId, request.CommandId, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(preCapturedSnapshot.SnapshotId) ||
            preCapturedSnapshot.ReverseState.ValueKind == JsonValueKind.Undefined ||
            preCapturedSnapshot.CreatedUtc.Kind != DateTimeKind.Utc ||
            snapshotAge < TimeSpan.Zero || snapshotAge > MaximumSnapshotAge)
        {
            return CommandHandlerOutcome.Blocked(
                "execution.snapshot_mismatch",
                "The undo snapshot must identify the same command as the request.");
        }

        if (!TryGetCompensator(preCapturedSnapshot.CommandId, out _))
        {
            return CommandHandlerOutcome.Blocked(
                "execution.compensator_unavailable",
                $"No rollback compensator is registered for '{preCapturedSnapshot.CommandId}'.");
        }

        if (!_usedSnapshotIds.TryAdd(preCapturedSnapshot.SnapshotId, 0))
        {
            return CommandHandlerOutcome.Blocked(
                "execution.snapshot_reused",
                "The undo snapshot has already been used or is still active.");
        }

        CommandResult result = await dispatcher.ExecuteAsync(
            request,
            new CommandExecutionOptions(ReviewApproved: request.Approval is not null),
            cancellationToken).ConfigureAwait(false);

        var outcome = new CommandHandlerOutcome(result.Status, result.Code, result.Message, result.Data, result.UndoAvailable);

        if (outcome.Status == CommandResultStatus.Succeeded)
        {
            _undoableSnapshots.TryAdd(preCapturedSnapshot.SnapshotId, preCapturedSnapshot);
            TriggerUndoNotification(preCapturedSnapshot.SnapshotId, request.CommandId, humanDescription, preCapturedSnapshot);
        }

        return outcome;
    }

    private void TriggerUndoNotification(string snapshotId, string commandId, string description, StateSnapshot snapshot)
    {
        if (CurrentNotification != null)
        {
            DismissNotification(CurrentNotification.SnapshotId);
        }

        var cts = new CancellationTokenSource();
        if (!_activeTimers.TryAdd(snapshotId, cts))
        {
            cts.Dispose();
            return;
        }

        var notification = new UndoNotification(
            snapshotId,
            commandId,
            description,
            durationSeconds: 10,
            onUndo: async () => await ExecuteRollbackAsync(snapshot),
            onDismiss: () => DismissNotification(snapshotId));

        CurrentNotification = notification;
        RaiseNotificationChanged(notification);
        _ = RunCountdownAsync(snapshotId, notification, cts);
    }

    private async Task RunCountdownAsync(string snapshotId, UndoNotification notification, CancellationTokenSource timer)
    {
        try
        {
            for (int secondsRemaining = notification.DurationSeconds; secondsRemaining > 0; secondsRemaining--)
            {
                notification.RemainingSeconds = secondsRemaining;
                notification.ProgressPercentage = secondsRemaining * 100.0 / notification.DurationSeconds;
                RaiseNotificationChanged(notification);
                await Task.Delay(TimeSpan.FromSeconds(1), timer.Token);
            }
            DismissNotification(snapshotId);
        }
        catch (OperationCanceledException) when (timer.IsCancellationRequested)
        {
            // The user dismissed the notification or used the undo action.
        }
    }

    private void RaiseNotificationChanged(UndoNotification? notification)
    {
        EventHandler<UndoNotification?>? handlers = NotificationChanged;
        if (handlers is null) return;
        foreach (EventHandler<UndoNotification?> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, notification);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Undo notification subscriber failed: {exception}");
            }
        }
    }

    public async Task<bool> ExecuteRollbackAsync(StateSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot is null || snapshot.IsReverted || string.IsNullOrWhiteSpace(snapshot.SnapshotId) ||
            string.IsNullOrWhiteSpace(snapshot.CommandId) || cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        // A transient toast can receive repeated clicks while its first compensation is in flight.
        // Claim the immutable snapshot id before invoking external code so the same reverse state is
        // never applied concurrently or twice after a successful compensation.
        if (!_undoableSnapshots.TryGetValue(snapshot.SnapshotId, out StateSnapshot? activeSnapshot) ||
            !string.Equals(activeSnapshot.CommandId, snapshot.CommandId, StringComparison.OrdinalIgnoreCase) ||
            !_rollbackClaims.TryAdd(snapshot.SnapshotId, 0))
        {
            return false;
        }

        DismissNotification(snapshot.SnapshotId);

        bool invoked = false;
        try
        {
            if (!TryGetCompensator(snapshot.CommandId, out ICompensatorDefinition compensator))
            {
                _rollbackClaims.TryRemove(snapshot.SnapshotId, out _);
                return false;
            }

            invoked = true;
            return await compensator.CompensateAsync(snapshot.ReverseState, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // A missing compensator is safe to retry after it is registered. Once invoked, retain
            // the claim even on a negative result because StateSnapshot cannot record partial work.
            if (!invoked) _rollbackClaims.TryRemove(snapshot.SnapshotId, out _);
        }
    }

    private bool TryGetCompensator(string commandId, out ICompensatorDefinition compensator)
    {
        if (_compensators.TryGetValue(commandId, out ICompensatorDefinition? found) ||
            _compensators.TryGetValue("compensator." + commandId, out found))
        {
            compensator = found;
            return true;
        }

        compensator = null!;
        return false;
    }

    public void DismissNotification(string snapshotId)
    {
        _undoableSnapshots.TryRemove(snapshotId, out _);
        if (_activeTimers.TryRemove(snapshotId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        if (CurrentNotification?.SnapshotId == snapshotId)
        {
            CurrentNotification = null;
            RaiseNotificationChanged(null);
        }
    }

    public ValueTask DisposeAsync()
    {
        foreach (var cts in _activeTimers.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _activeTimers.Clear();
        _usedSnapshotIds.Clear();
        _undoableSnapshots.Clear();
        CurrentNotification = null;
        return ValueTask.CompletedTask;
    }
}
