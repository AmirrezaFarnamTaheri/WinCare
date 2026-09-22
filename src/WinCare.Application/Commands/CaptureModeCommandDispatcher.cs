using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands;

/// <summary>
/// Command-plane boundary for documentation capture sessions. Every
/// <see cref="ICommandDispatcher.ExecuteAsync(CommandRequest, CommandExecutionOptions, CancellationToken)"/>
/// is rejected, because a route rendered for the documentation must never mutate the machine it
/// documents. Dynamic command registration is still delegated to the inner dispatcher: plugin
/// discovery registers handlers, it does not run them, and capture needs the same catalog the
/// documented pages display.
/// </summary>
/// <remarks>
/// This is the runtime half of the read-only capture contract; the source-text gate in
/// <c>tests/native/test_screenshot_pipeline.py</c> is its static half. Rejecting execution
/// means the failure mode is "capture run fails loudly" rather than "a refactor quietly
/// introduces a dispatch the text scan never names".
/// </remarks>
public sealed class CaptureModeCommandDispatcher : ICommandDispatcher
{
    private readonly ICommandDispatcher _inner;
    private readonly List<CommandRequest> _rejectedRequests = new();
    private readonly object _rejectedLock = new();

    /// <summary>Creates a rejecting wrapper over the real dispatcher.</summary>
    /// <param name="inner">The dispatcher that would normally execute commands.</param>
    /// <param name="caller">
    /// Optional capture-line annotation so a stack trace names the composition site.
    /// </param>
    public CaptureModeCommandDispatcher(ICommandDispatcher inner, [CallerMemberName] string? caller = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _caller = caller;
    }

    private readonly string? _caller;

    /// <summary>Every request the wrapper has rejected this session, in arrival order.</summary>
    public IReadOnlyList<CommandRequest> RejectedRequests
    {
        get
        {
            lock (_rejectedLock)
            {
                return _rejectedRequests.ToImmutableArray();
            }
        }
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteAsync(
        CommandRequest request,
        CommandExecutionOptions options,
        CancellationToken cancellationToken)
    {
        lock (_rejectedLock)
        {
            _rejectedRequests.Add(request);
        }
        throw new InvalidOperationException(
            $"A documentation capture session attempted to dispatch command '{request.CommandId}'"
            + (_caller is null ? "" : $" (composed by {_caller})")
            + ". Capture renders documented routes only; it must never execute a command.");
    }

    /// <inheritdoc />
    public bool RegisterDynamicCommand(CommandDefinition definition, ICommandHandler handler) =>
        _inner.RegisterDynamicCommand(definition, handler);

    /// <inheritdoc />
    public bool UnregisterDynamicCommand(string commandId) => _inner.UnregisterDynamicCommand(commandId);
}
