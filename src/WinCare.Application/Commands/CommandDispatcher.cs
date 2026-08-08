using System.Collections.ObjectModel;
using System.Text.Json;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands;

/// <summary>
/// Admission, policy, and dispatch for the native WinCare command plane.
/// </summary>
public sealed class CommandDispatcher
{
    private readonly IReadOnlyDictionary<string, CommandDefinition> _definitions;
    private readonly IReadOnlyDictionary<string, ICommandHandler> _handlers;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new dispatcher bound to the catalog and the supplied handlers.
    /// </summary>
    public CommandDispatcher(
        IReadOnlyList<CommandDefinition> definitions,
        IEnumerable<ICommandHandler> handlers,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(handlers);

        Dictionary<string, CommandDefinition> definitionsById = new(StringComparer.Ordinal);
        foreach (CommandDefinition definition in definitions)
        {
            if (!definitionsById.TryAdd(definition.Id, definition))
            {
                throw new ArgumentException($"Duplicate command definition '{definition.Id}'.", nameof(definitions));
            }
        }

        Dictionary<string, ICommandHandler> handlersById = new(StringComparer.Ordinal);
        foreach (ICommandHandler handler in handlers)
        {
            ArgumentNullException.ThrowIfNull(handler);
            if (string.IsNullOrWhiteSpace(handler.CommandId))
            {
                throw new ArgumentException("Every command handler requires an ID.", nameof(handlers));
            }
            if (!definitionsById.ContainsKey(handler.CommandId))
            {
                throw new ArgumentException(
                    $"Handler '{handler.CommandId}' has no command definition.",
                    nameof(handlers));
            }
            if (!handlersById.TryAdd(handler.CommandId, handler))
            {
                throw new ArgumentException($"Duplicate command handler '{handler.CommandId}'.", nameof(handlers));
            }
        }

        _definitions = new ReadOnlyDictionary<string, CommandDefinition>(definitionsById);
        _handlers = new ReadOnlyDictionary<string, ICommandHandler>(handlersById);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Executes a typed command request through admission and, when admitted, the registered handler.
    /// </summary>
    public async Task<CommandResult> ExecuteAsync(
        CommandRequest request,
        CommandExecutionOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        options ??= CommandExecutionOptions.Default;
        DateTimeOffset startedAt = _timeProvider.GetUtcNow();

        if (cancellationToken.IsCancellationRequested)
        {
            return CreateResult(
                request,
                CommandResultStatus.Cancelled,
                "command.cancelled",
                "The command was cancelled before it started.",
                data: null,
                undoAvailable: false,
                startedAt);
        }

        if (options.Deadline is DateTimeOffset deadline && deadline <= startedAt)
        {
            return CreateResult(
                request,
                CommandResultStatus.Cancelled,
                "command.deadline_exceeded",
                "The command deadline passed before execution started.",
                data: null,
                undoAvailable: false,
                startedAt);
        }

        if (string.IsNullOrWhiteSpace(request.CommandId) ||
            !_definitions.TryGetValue(request.CommandId, out CommandDefinition? definition))
        {
            return CreateResult(
                request,
                CommandResultStatus.Blocked,
                "command.unknown",
                "The requested command is not in the WinCare catalog.",
                data: null,
                undoAvailable: false,
                startedAt);
        }

        if (request.Parameters.ValueKind != JsonValueKind.Object)
        {
            return CreateResult(
                request,
                CommandResultStatus.Blocked,
                "command.parameters_invalid",
                "Command parameters must be a JSON object.",
                data: null,
                undoAvailable: false,
                startedAt);
        }

        if (definition.MigrationStatus is not MigrationStatus.Implemented and not MigrationStatus.BehaviorVerified)
        {
            return CreateResult(
                request,
                CommandResultStatus.NotMigrated,
                "command.not_migrated",
                "This command is preserved in the catalog but its native implementation is not ready.",
                data: null,
                undoAvailable: false,
                startedAt);
        }

        if (definition.ReadOnly && request.Apply)
        {
            return CreateResult(
                request,
                CommandResultStatus.Blocked,
                "command.read_only",
                "This command only reads system information and cannot apply changes.",
                data: null,
                undoAvailable: false,
                startedAt);
        }

        if (!definition.ReadOnly && request.Apply && !options.ReviewApproved)
        {
            return CreateResult(
                request,
                CommandResultStatus.Blocked,
                "command.review_required",
                "Review and approve the planned changes before applying this command.",
                data: null,
                undoAvailable: false,
                startedAt);
        }

        if (!_handlers.TryGetValue(definition.Id, out ICommandHandler? handler))
        {
            return CreateResult(
                request,
                CommandResultStatus.Failed,
                "command.handler_missing",
                "The native command registration is incomplete.",
                data: null,
                undoAvailable: false,
                startedAt);
        }

        using CancellationTokenSource linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.Deadline is DateTimeOffset activeDeadline)
        {
            TimeSpan remaining = activeDeadline - startedAt;
            if (remaining <= TimeSpan.FromMilliseconds(int.MaxValue))
            {
                linkedCancellation.CancelAfter(remaining);
            }
        }

        try
        {
            CommandHandlerOutcome outcome = await handler.ExecuteAsync(
                request,
                linkedCancellation.Token).ConfigureAwait(false);
            return CreateResult(
                request,
                outcome.Status,
                outcome.Code,
                outcome.Message,
                outcome.Data,
                outcome.UndoAvailable,
                startedAt);
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            bool deadlineExceeded = !cancellationToken.IsCancellationRequested &&
                options.Deadline is DateTimeOffset configuredDeadline &&
                configuredDeadline <= _timeProvider.GetUtcNow();
            return CreateResult(
                request,
                CommandResultStatus.Cancelled,
                deadlineExceeded ? "command.deadline_exceeded" : "command.cancelled",
                deadlineExceeded
                    ? "The command did not complete before its deadline."
                    : "The command was cancelled.",
                data: null,
                undoAvailable: false,
                startedAt);
        }
        catch (Exception)
        {
            return CreateResult(
                request,
                CommandResultStatus.Failed,
                "command.failed",
                "The command could not be completed. No changes were reported as applied.",
                data: null,
                undoAvailable: false,
                startedAt);
        }
    }

    private CommandResult CreateResult(
        CommandRequest request,
        CommandResultStatus status,
        string code,
        string message,
        JsonElement? data,
        bool undoAvailable,
        DateTimeOffset startedAt) =>
        new(
            request.CommandId,
            request.CorrelationId,
            status,
            code,
            message,
            data,
            startedAt,
            _timeProvider.GetUtcNow(),
            undoAvailable);
}
