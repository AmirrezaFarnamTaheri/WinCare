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
/// Coordinator managing optimistic one-touch command execution and transient undo windows.
/// Replaces defensive multi-step friction for Safe and Moderate actions with automatic state snapshotting.
/// </summary>
public sealed class TransientUndoCoordinator : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeTimers = new();
    private readonly ConcurrentDictionary<string, ICompensatorDefinition> _compensators = new(StringComparer.OrdinalIgnoreCase);

    public UndoNotification? CurrentNotification { get; private set; }

    public event EventHandler<UndoNotification?>? NotificationChanged;

    /// <summary>
    /// Registers a compensator for state rollback operations.
    /// </summary>
    public void RegisterCompensator(ICompensatorDefinition compensator)
    {
        if (compensator == null) throw new ArgumentNullException(nameof(compensator));
        _compensators[compensator.CompensatorId] = compensator;
    }

    /// <summary>
    /// Executes an operation optimistically, captures a rollback snapshot, and presents a transient undo toast.
    /// </summary>
    public async Task<CommandHandlerOutcome> ExecuteOptimisticAsync(
        ISubsystemCommandExecutor executor,
        CommandDefinition definition,
        CommandRequest request,
        string humanDescription,
        StateSnapshot? preCapturedSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        if (executor == null) throw new ArgumentNullException(nameof(executor));
        if (definition != null && definition.Risk is CommandRisk.Critical)
        {
            return CommandHandlerOutcome.Blocked(
                "execution.critical_guarded",
                $"Command '{request.CommandId}' is a Critical operation and requires explicit two-phase cryptographic confirmation.");
        }

        var outcome = await executor.ExecuteAsync(definition!, request, cancellationToken);

        if (outcome.Success && preCapturedSnapshot != null)
        {
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
        _activeTimers[snapshotId] = cts;

        var notification = new UndoNotification(
            snapshotId,
            commandId,
            description,
            durationSeconds: 10,
            onUndo: async () => await ExecuteRollbackAsync(snapshot),
            onDismiss: () => DismissNotification(snapshotId));

        CurrentNotification = notification;
        NotificationChanged?.Invoke(this, notification);

        _ = Task.Run(async () =>
        {
            try
            {
                for (int i = 10; i > 0; i--)
                {
                    notification.RemainingSeconds = i;
                    notification.ProgressPercentage = (i / 10.0) * 100.0;
                    NotificationChanged?.Invoke(this, notification);
                    await Task.Delay(1000, cts.Token);
                }
                DismissNotification(snapshotId);
            }
            catch (OperationCanceledException)
            {
                // Timer cancelled on manual undo or dismissal
            }
        }, cts.Token);
    }

    public async Task<bool> ExecuteRollbackAsync(StateSnapshot snapshot)
    {
        if (snapshot == null || snapshot.IsReverted) return false;

        DismissNotification(snapshot.SnapshotId);

        if (_compensators.TryGetValue(snapshot.CommandId, out var compensator) ||
            _compensators.TryGetValue("compensator." + snapshot.CommandId, out compensator))
        {
            return await compensator.CompensateAsync(snapshot.ReverseState, CancellationToken.None);
        }

        return false;
    }

    public void DismissNotification(string snapshotId)
    {
        if (_activeTimers.TryRemove(snapshotId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        if (CurrentNotification?.SnapshotId == snapshotId)
        {
            CurrentNotification = null;
            NotificationChanged?.Invoke(this, null);
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
        return ValueTask.CompletedTask;
    }
}
