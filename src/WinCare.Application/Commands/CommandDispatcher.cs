using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Security.Principal;
using System.Text.Json;
using WinCare.Application.Activity;
using WinCare.Application.Native;
using WinCare.CommandCatalog;
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
    private readonly Func<bool> _isProcessElevated;
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
    /// <param name="definitions">Catalog command definitions the dispatcher admits.</param>
    /// <param name="handlers">Handlers bound to admitted command IDs.</param>
    /// <param name="timeProvider">Clock used for approval-plan expiry and timestamps; defaults to the system clock.</param>
    /// <param name="nativeCore">Optional native core interop used by system probes; defaults to the loaded core.</param>
    /// <param name="journal">Optional activity journal receiving dispatch records.</param>
    /// <param name="isProcessElevated">
    /// Optional override for the process elevation probe used by the administrator-access gate.
    /// Production callers omit it and get the real <see cref="WindowsPrincipal"/> check; tests that
    /// exercise elevated commands supply a deterministic value instead of depending on how the test
    /// host was launched. The gate itself is enforced either way.
    /// </param>
    public CommandDispatcher(
        IReadOnlyList<CommandDefinition> definitions,
        IEnumerable<ICommandHandler> handlers,
        TimeProvider? timeProvider = null,
        INativeCoreService? nativeCore = null,
        IActivityJournalService? journal = null,
        Func<bool>? isProcessElevated = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(handlers);
        _journal = journal;
        _isProcessElevated = isProcessElevated ?? IsCurrentProcessElevated;

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
                throw new InvalidOperationException(
                    "wincare_core.dll could not be loaded. " +
                    "Build the native wincare-core project or run the native staging step before launching, " +
                    "then ensure wincare_core.dll is next to the app executable. " +
                    $"Underlying error: {ex.Message}", ex);
            }
        }

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

        if (string.IsNullOrWhiteSpace(handler.CommandId) ||
            !string.Equals(handler.CommandId, definition.Id, StringComparison.OrdinalIgnoreCase))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[CommandDispatcher] Dynamic registration rejected: handler id '{handler.CommandId}' does not match definition id '{definition.Id}'.");
            return false;
        }

        if (_definitions.ContainsKey(definition.Id) ||
            definition.Id.StartsWith("wincare.core.", StringComparison.OrdinalIgnoreCase) ||
            definition.Id.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
        {
            System.Diagnostics.Debug.WriteLine($"[CommandDispatcher] Dynamic registration rejected: '{definition.Id}' collides with reserved core namespace.");
            return false;
        }

        definition = WinCare.Application.Plugins.PluginCommandPolicy.Normalize(definition);
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

        if (string.IsNullOrWhiteSpace(request.CommandId))
        {
            return CreateResult(request, CommandResultStatus.Blocked, "command.id_required",
                "The request does not identify a command.", null, false, startedAt);
        }

        if (request.Parameters.ValueKind != JsonValueKind.Object)
        {
            return CreateResult(request, CommandResultStatus.Blocked, "command.parameters_invalid",
                "Command parameters must be a JSON object.", null, false, startedAt);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CreateResult(request, CommandResultStatus.Cancelled, "command.cancelled",
                "The operation was cancelled.", null, false, startedAt);
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
            return CreateResult(request, CommandResultStatus.Blocked, "command.not_found",
                $"Command '{request.CommandId}' is not declared in the native catalog.", null, false, startedAt);
        }

        // Validate against the command's own schema on every dispatch path. Core definitions use
        // the generated schema table; extension-pack definitions carry their schema in the fragment.
        IReadOnlyList<string> parameterErrors = CommandParameterValidator.Validate(
            definition.Id,
            request.Parameters,
            definition.Parameters);
        if (parameterErrors.Count > 0)
        {
            return CreateResult(request, CommandResultStatus.Blocked, "command.parameters_invalid",
                parameterErrors[0], null, false, startedAt);
        }

        if (definition.MigrationStatus is not (MigrationStatus.Implemented or MigrationStatus.BehaviorVerified))
        {
            return CreateResult(request, CommandResultStatus.NotMigrated, "command.migration_blocked",
                $"Command '{request.CommandId}' is cataloged as '{definition.MigrationStatus}' and cannot be executed.", null, false, startedAt);
        }

        if (handler is null)
        {
            return CreateResult(request, CommandResultStatus.NotMigrated, "command.not_migrated",
                $"Command '{request.CommandId}' has no native handler implementation registered.", null, false, startedAt);
        }

        if (definition.ReadOnly && request.Apply)
        {
            return CreateResult(request, CommandResultStatus.Blocked, "command.readonly_mutation_denied",
                $"Command '{request.CommandId}' is declared ReadOnly and cannot be invoked with Apply=true.", null, false, startedAt);
        }

        // An AdministratorAccess declaration is enforced at admission, not merely displayed. A
        // mutating command that requires elevation must never reach its handler in a non-elevated
        // process: failing later at native-execution time would already have promised the user an
        // operation this process is not entitled to perform. Read-only previews stay available so
        // the surface can explain what elevation is needed for.
        if (!definition.ReadOnly && request.Apply &&
            definition.AdministratorAccess == AdministratorAccess.Required &&
            !_isProcessElevated())
        {
            return CreateResult(request, CommandResultStatus.Blocked, "command.elevation_required",
                $"Command '{request.CommandId}' requires administrator access. Relaunch WinCare as an administrator, then run this command again.", null, false, startedAt);
        }

        if (!definition.ReadOnly && request.Apply)
        {
            RiskTier riskTier = definition.RiskTier;
            if (riskTier == RiskTier.Destructive)
            {
                if (!options.ReviewApproved)
                {
                    return CreateResult(request, CommandResultStatus.Blocked, "command.review_required",
                        $"Destructive command '{request.CommandId}' requires explicit ReviewApproved confirmation.", null, false, startedAt);
                }

                if (!TryConsumeIssuedReviewPlan(request, definition))
                {
                    return CreateResult(request, CommandResultStatus.Blocked, "command.approval_plan_invalid",
                        $"Destructive command '{request.CommandId}' requires a current, single-use review plan issued by this dispatcher after a successful preview.", null, false, startedAt);
                }
            }
            else if (riskTier == RiskTier.Moderate)
            {
                if (!options.ReviewApproved)
                {
                    return CreateResult(request, CommandResultStatus.Blocked, "command.review_required",
                        $"Mutating command '{request.CommandId}' requires explicit ReviewApproved confirmation.", null, false, startedAt);
                }

                // If a review plan was supplied, validate and consume it; otherwise admit directly with user confirmation.
                if (request.Approval is not null && !TryConsumeIssuedReviewPlan(request, definition))
                {
                    return CreateResult(request, CommandResultStatus.Blocked, "command.approval_plan_invalid",
                        $"Mutating command '{request.CommandId}' supplied an invalid, expired, or already-consumed review plan.", null, false, startedAt);
                }
            }
            else if (riskTier != RiskTier.Safe)
            {
                return CreateResult(request, CommandResultStatus.Blocked, "command.risk_tier_invalid",
                    $"Command '{request.CommandId}' has an unsupported risk tier.", null, false, startedAt);
            }
            // RiskTier.Safe mutating commands are admitted directly with Apply=true.
        }

        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.Deadline is DateTimeOffset deadline)
        {
            TimeSpan delay = deadline - startedAt;
            if (delay <= TimeSpan.Zero)
            {
                return CreateResult(request, CommandResultStatus.Cancelled, "command.deadline_exceeded",
                    "The command deadline has already expired.", null, false, startedAt);
            }
            linkedCancellation.CancelAfter(delay);
        }

        ActivityRecord? activity = _journal?.Begin(definition.Id, definition.Title ?? definition.Id);

        try
        {
            CommandHandlerOutcome outcome = await handler.ExecuteAsync(request, linkedCancellation.Token).ConfigureAwait(false);

            if (activity is not null)
            {
                if (outcome.Status == CommandResultStatus.Succeeded)
                {
                    // Undo is exposed only when the dispatcher has a concrete executable compensator.
                    // Handler metadata alone is not sufficient to promise a recoverable action.
                    _journal?.Complete(activity.Id, outcome.Message, undoAvailable: false);
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
                reviewPlan = IssueReviewPlan(
                    definition.Id,
                    request.Parameters,
                    request.CorrelationId,
                    ExecutionDigestFromPreview(definition.Id, outcome.Data));
            }

            return CreateResult(
                request,
                outcome.Status,
                outcome.Code,
                outcome.Message,
                outcome.Data,
                undoAvailable: false,
                startedAt,
                reviewPlan,
                activity);
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
            // A cancelled mutating command may have applied part of its work before cancellation;
            // provide clear guidance to verify affected state.
            bool mutationStateUnknown = request.Apply && !definition.ReadOnly;
            string cancelledMessage = deadlineExceeded
                ? "The command did not complete before its deadline."
                : "The command was cancelled.";
            if (mutationStateUnknown)
            {
                cancelledMessage += " The command may have applied part of its work before cancellation; inspect Activity and verify the affected system state before retrying.";
            }
            return CreateResult(
                request,
                CommandResultStatus.Cancelled,
                deadlineExceeded ? "command.deadline_exceeded" : "command.cancelled",
                cancelledMessage,
                null,
                false,
                startedAt);
        }
        catch (Exception ex)
        {
            bool finalStateUnknown = request.Apply && !definition.ReadOnly;
            string safeFailureMessage = finalStateUnknown
                ? "The command faulted after mutation execution began. The final system state is unknown; inspect Activity and verify the affected system state before retrying."
                : "The command could not be completed. No mutating execution was admitted for this request.";

            if (activity is not null)
            {
                _journal?.Fail(activity.Id, finalStateUnknown
                    ? $"Command faulted ({ex.GetType().Name}); final system state is unknown. Verify the affected resource before retrying."
                    : $"Command faulted ({ex.GetType().Name}) before any admitted mutation. Review the request before retrying.");
            }
            System.Diagnostics.Debug.WriteLine($"[CommandDispatcher] {request.CommandId} fault: {ex}");

            return CreateResult(request, CommandResultStatus.Failed,
                finalStateUnknown ? "command.failed_state_unknown" : "command.failed",
                safeFailureMessage, null, false, startedAt);
        }
    }

    private ApprovedMutationPlan IssueReviewPlan(string commandId, JsonElement parameters, Guid correlationId, string? executionDigest)
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
            ApprovedMutationPlan.NewPlanId(),
            commandId,
            ApprovedMutationPlan.ComputeCanonicalDigest(parameters),
            now,
            correlationId,
            executionDigest);
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

        if (!_issuedReviewPlans.TryGetValue(submitted.PlanId, out ApprovedMutationPlan? issued) || !Equals(issued, submitted))
        {
            return false;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        TimeSpan age = now - issued.ApprovedAtUtc;
        if (age < TimeSpan.Zero || age > ReviewPlanLifetime ||
            issued.CorrelationId == Guid.Empty ||
            issued.CorrelationId != request.CorrelationId ||
            !string.Equals(issued.CommandId, definition.Id, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(issued.ParametersDigest, ApprovedMutationPlan.ComputeCanonicalDigest(request.Parameters), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(issued.ExecutionDigest, CurrentExecutionDigest(definition.Id, request.Parameters, submitted.ExecutionDigest), StringComparison.OrdinalIgnoreCase))
        {
            _issuedReviewPlans.TryRemove(submitted.PlanId, out _);
            return false;
        }

        return _issuedReviewPlans.TryRemove(submitted.PlanId, out _);
    }

    private static string? ExecutionDigestFromPreview(string commandId, JsonElement? previewData)
    {
        if (commandId is not (CommandIds.Preset or CommandIds.RemediationRestore) || previewData is not JsonElement data ||
            data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(CommandIds.ExecutionDigestPropertyName, out JsonElement digest) ||
            digest.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return digest.GetString();
    }

    private static string? CurrentExecutionDigest(string commandId, JsonElement parameters, string? submittedExecutionDigest)
    {
        if (commandId.Equals(CommandIds.RemediationRestore, StringComparison.OrdinalIgnoreCase))
            return submittedExecutionDigest;
        if (!commandId.Equals(CommandIds.Preset, StringComparison.OrdinalIgnoreCase) ||
            !parameters.TryGetProperty(CommandIds.PresetIdPropertyName, out JsonElement presetId) || presetId.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(presetId.GetString()))
        {
            return null;
        }

        RemediationPresetPlan plan = RemediationPresetPlanner.Create(
            presetId.GetString()!,
            WinCare.CommandCatalog.RemediationCatalog.LoadPresets(),
            WinCare.CommandCatalog.RemediationCatalog.LoadRules(),
            Environment.OSVersion.Version.Build,
            IsCurrentProcessElevated());
        return plan.IsExecutable ? plan.Digest : null;
    }

    /// <summary>
    /// Reports whether the current process runs with administrator privileges. Both the admission
    /// gate and the preset execution digest use this single probe so the two never disagree.
    /// </summary>
    private static bool IsCurrentProcessElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private CommandResult CreateResult(
        CommandRequest request,
        CommandResultStatus status,
        string code,
        string message,
        JsonElement? data,
        bool undoAvailable,
        DateTimeOffset startedAt,
        ApprovedMutationPlan? reviewPlan = null,
        ActivityRecord? activity = null)
    {
        // Admission rejections (Blocked / NotMigrated) happen before a normal activity
        // record exists; log them so they remain visible in Activity history. The command id is
        // sanitized because a request that reaches admission with no usable id is exactly the
        // malformed case this path must report instead of throwing on.
        if (activity is null && _journal is not null &&
            status is CommandResultStatus.Blocked or CommandResultStatus.NotMigrated)
        {
            string journalCommandId = string.IsNullOrWhiteSpace(request.CommandId) ? "unknown" : request.CommandId;
            ActivityRecord rejection = _journal.Begin(journalCommandId, journalCommandId);
            _journal.Fail(rejection.Id, message);
        }

        return new(
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
}
