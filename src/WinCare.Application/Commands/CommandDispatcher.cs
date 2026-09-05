using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.Json;
using WinCare.Application.Activity;
using WinCare.Application.Native;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Activity;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands;

/// <summary>
/// Admission, policy, and dispatch for the native WinCare command plane.
/// </summary>
public sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly IReadOnlyDictionary<string, CommandDefinition> _definitions;
    private readonly IReadOnlyDictionary<string, ICommandHandler> _handlers;
    private readonly ConcurrentDictionary<string, (CommandDefinition Definition, ICommandHandler Handler)> _dynamicCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _timeProvider;
    private readonly IActivityJournalService? _journal;
    private readonly ConcurrentDictionary<string, ApprovedMutationPlan> _issuedReviewPlans = new(StringComparer.Ordinal);
    private static readonly TimeSpan ReviewPlanLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Expected C ABI version exported by <c>wincare_core</c>.
    /// </summary>
    public const uint SupportedAbiVersion = 1;

    /// <summary>
    /// Initializes a new dispatcher bound to the catalog and the supplied handlers.
    /// </summary>
    public CommandDispatcher(
        IReadOnlyList<CommandDefinition> definitions,
        IEnumerable<ICommandHandler> handlers,
        TimeProvider? timeProvider = null,
        INativeCoreService? nativeCore = null,
        IActivityJournalService? journal = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(handlers);
        _journal = journal;

        if (nativeCore is not null)
        {
            try
            {
                uint actual = nativeCore.GetAbiVersion();
                const uint expected = SupportedAbiVersion;
                if (actual != expected)
                {
                    throw new InvalidOperationException(
                        $"wincare_core ABI version mismatch: expected {expected}, got {actual}. " +
                        $"The installed wincare_core.dll is incompatible with this build. " +
                        $"Replace wincare_core.dll with a build matching ABI version {expected}.");
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                // The Release pipeline stages wincare_core.dll via an MSBuild target, but a
                // Debug/F5 run without it would otherwise surface a bare DllNotFoundException.
                // Fail with an actionable message instead (see App.OnUnhandledException).
                throw new InvalidOperationException(
                    "wincare_core.dll could not be loaded. " +
                    "Build the native wincare-core project or run the native staging step before launching, " +
                    "then ensure wincare_core.dll is next to the app executable. " +
                    $"Underlying error: {ex.Message}", ex);
            }
        }

        // Case-insensitive keying keeps static lookup consistent with the dynamic plugin
        // registry and with ApprovedMutationPlan.IsValid, which compare IDs ordinal-insensitively.
        Dictionary<string, CommandDefinition> definitionsById = new(StringComparer.OrdinalIgnoreCase);
        foreach (CommandDefinition definition in definitions)
        {
            if (!definitionsById.TryAdd(definition.Id, definition))
            {
                throw new ArgumentException($"Duplicate command definition '{definition.Id}'.", nameof(definitions));
            }
        }

        Dictionary<string, ICommandHandler> handlersById = new(StringComparer.OrdinalIgnoreCase);
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

    /// <inheritdoc />
    public bool RegisterDynamicCommand(CommandDefinition definition, ICommandHandler handler)
    {
        if (definition == null || handler == null || string.IsNullOrWhiteSpace(definition.Id))
        {
            return false;
        }

        // The static registration path requires the handler ID to match the definition ID;
        // enforce the same invariant for dynamic (plugin) commands so a mismatched handler
        // can never be resolved against a definition it was not built for.
        if (string.IsNullOrWhiteSpace(handler.CommandId) ||
            !string.Equals(handler.CommandId, definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[CommandDispatcher] Dynamic registration rejected: handler id '{handler.CommandId}' does not match definition id '{definition.Id}'.");
            return false;
        }

        // Security / Invariant Protection: Core commands and namespaces cannot be overridden by dynamic plugins
        if (_definitions.ContainsKey(definition.Id) ||
            definition.Id.StartsWith("wincare.core.", StringComparison.OrdinalIgnoreCase) ||
            definition.Id.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
        {
            System.Diagnostics.Debug.WriteLine($"[CommandDispatcher] Dynamic registration rejected: '{definition.Id}' collides with reserved core namespace.");
            return false;
        }

        // Publish policy and implementation together; never replace another plugin's command.
        return _dynamicCommands.TryAdd(definition.Id, (definition, handler));
    }

    /// <inheritdoc />
    public bool UnregisterDynamicCommand(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId)) return false;

        return _dynamicCommands.TryRemove(commandId, out _);
    }

    /// <summary>
    /// Executes a typed command request through admission and, when admitted, the registered handler.
    /// </summary>
    public async Task<CommandResult> ExecuteAsync(
        CommandRequest request,
        CommandExecutionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset startedAt = _timeProvider.GetUtcNow();

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

        if (cancellationToken.IsCancellationRequested)
        {
            return CreateResult(
                request,
                CommandResultStatus.Cancelled,
                "command.cancelled",
                "The operation was cancelled.",
                data: null,
                undoAvailable: false,
                startedAt);
        }

        CommandDefinition? definition;
        ICommandHandler? handler;
        if (!string.IsNullOrWhiteSpace(request.CommandId) &&
            _dynamicCommands.TryGetValue(request.CommandId, out var registration))
        {
            (definition, handler) = registration;
        }
        else
        {
            _definitions.TryGetValue(request.CommandId ?? string.Empty, out definition);
            _handlers.TryGetValue(request.CommandId ?? string.Empty, out handler);
        }

        if (definition is null)
        {
            return CreateResult(
                request,
                CommandResultStatus.Blocked,
                "command.not_found",
                $"Command '{request.CommandId}' is not declared in the native catalog.",
                data: null,
                undoAvailable: false,
                startedAt);
        }

        if (definition.MigrationStatus is not (MigrationStatus.Implemented or MigrationStatus.BehaviorVerified))
        {
            return CreateResult(
                request,
                CommandResultStatus.NotMigrated,
                "command.migration_blocked",
                $"Command '{request.CommandId}' is cataloged as '{definition.MigrationStatus}' and cannot be executed.",
                data: null,
                undoAvailable: false,
                startedAt);
        }

        if (handler is null)
        {
            return CreateResult(
                request,
                CommandResultStatus.NotMigrated,
                "command.not_migrated",
                $"Command '{request.CommandId}' has no native handler implementation registered.",
                data: null,
                undoAvailable: false,
                startedAt);
        }

        if (definition.ReadOnly && request.Apply)
        {
            return CreateResult(
                request,
                CommandResultStatus.Blocked,
                "command.readonly_mutation_denied",
                $"Command '{request.CommandId}' is declared ReadOnly and cannot be invoked with Apply=true.",
                data: null,
                undoAvailable: false,
                startedAt);
        }

        if (!definition.ReadOnly && request.Apply)
        {
            if (!options.ReviewApproved)
            {
                return CreateResult(
                    request,
                    CommandResultStatus.Blocked,
                    "command.review_required",
                    $"Mutating command '{request.CommandId}' requires explicit ReviewApproved confirmation.",
                    data: null,
                    undoAvailable: false,
                    startedAt);
            }

            if (!TryConsumeIssuedReviewPlan(request, definition))
            {
                return CreateResult(
                    request,
                    CommandResultStatus.Blocked,
                    "command.approval_plan_invalid",
                    $"Mutating command '{request.CommandId}' requires a current, single-use review plan issued by this dispatcher after a successful preview.",
                    data: null,
                    undoAvailable: false,
                    startedAt);
            }
        }

        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.Deadline is DateTimeOffset deadline)
        {
            TimeSpan delay = deadline - startedAt;
            if (delay <= TimeSpan.Zero)
            {
                return CreateResult(
                    request,
                    CommandResultStatus.Cancelled,
                    "command.deadline_exceeded",
                    "The command deadline has already expired.",
                    data: null,
                    undoAvailable: false,
                    startedAt);
            }
            linkedCancellation.CancelAfter(delay);
        }

        ActivityRecord? activity = _journal?.Begin(definition.Id, definition.Title ?? definition.Id);

        try
        {
            CommandHandlerOutcome outcome = await handler.ExecuteAsync(
                request,
                linkedCancellation.Token).ConfigureAwait(false);

            if (activity is not null)
            {
                if (outcome.Status == CommandResultStatus.Succeeded)
                {
                    _journal?.Complete(activity.Id, outcome.Message, outcome.UndoAvailable);
                }
                else if (outcome.Status == CommandResultStatus.Cancelled)
                {
                    _journal?.Cancel(activity.Id);
                }
                else
                {
                    _journal?.Fail(activity.Id, outcome.Message);
                }
            }

            ApprovedMutationPlan? reviewPlan = null;
            if (!definition.ReadOnly && !request.Apply && outcome.Status == CommandResultStatus.Succeeded)
            {
                reviewPlan = IssueReviewPlan(definition.Id, request.Parameters, request.CorrelationId);
            }

            return CreateResult(
                request,
                outcome.Status,
                outcome.Code,
                outcome.Message,
                outcome.Data,
                outcome.UndoAvailable,
                startedAt,
                reviewPlan);
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            if (activity is not null)
            {
                _journal?.Cancel(activity.Id);
            }
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
        catch (Exception ex)
        {
            if (activity is not null)
            {
                // Log only the exception type — ex.Message can contain file paths or PII.
                _journal?.Fail(activity.Id, $"Command faulted ({ex.GetType().Name}). Review the system state before retrying.");
                System.Diagnostics.Debug.WriteLine($"[CommandDispatcher] {request.CommandId} fault: {ex}");
            }
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

    private ApprovedMutationPlan IssueReviewPlan(string commandId, JsonElement parameters, Guid correlationId)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach ((string planId, ApprovedMutationPlan plan) in _issuedReviewPlans)
        {
            if (now - plan.ApprovedAtUtc > ReviewPlanLifetime)
            {
                _issuedReviewPlans.TryRemove(planId, out _);
            }
        }

        ApprovedMutationPlan issued = new(
            "AMP-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            commandId,
            ApprovedMutationPlan.ComputeCanonicalDigest(parameters),
            now,
            correlationId);
        _issuedReviewPlans[issued.PlanId] = issued;
        return issued;
    }

    private bool TryConsumeIssuedReviewPlan(CommandRequest request, CommandDefinition definition)
    {
        ApprovedMutationPlan? submitted = request.Approval;
        if (submitted is null || string.IsNullOrWhiteSpace(submitted.PlanId))
        {
            return false;
        }

        if (!_issuedReviewPlans.TryGetValue(submitted.PlanId, out ApprovedMutationPlan? issued) ||
            !Equals(issued, submitted))
        {
            return false;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        TimeSpan age = now - issued.ApprovedAtUtc;
        if (age < TimeSpan.Zero || age > ReviewPlanLifetime ||
            issued.CorrelationId == Guid.Empty ||
            issued.CorrelationId != request.CorrelationId ||
            !string.Equals(issued.CommandId, definition.Id, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(issued.ParametersDigest, ApprovedMutationPlan.ComputeCanonicalDigest(request.Parameters), StringComparison.OrdinalIgnoreCase))
        {
            _issuedReviewPlans.TryRemove(submitted.PlanId, out _);
            return false;
        }

        // Atomic removal makes a review plan single-use. If another execution consumed it
        // first, this request fails closed and must preview again.
        return _issuedReviewPlans.TryRemove(submitted.PlanId, out _);
    }

    private CommandResult CreateResult(
        CommandRequest request,
        CommandResultStatus status,
        string code,
        string message,
        JsonElement? data,
        bool undoAvailable,
        DateTimeOffset startedAt,
        ApprovedMutationPlan? reviewPlan = null) =>
        new(
            request.CommandId,
            request.CorrelationId,
            status,
            code,
            message,
            data,
            startedAt,
            _timeProvider.GetUtcNow(),
            undoAvailable,
            reviewPlan);
}
