using WinCare.Domain.Commands;

namespace WinCare.Application.Commands;

/// <summary>
/// Admission, policy, and dispatch interface for the native WinCare command plane.
/// </summary>
public interface ICommandDispatcher
{
    /// <summary>
    /// Executes a typed command request through admission and, when admitted, the registered handler.
    /// </summary>
    Task<CommandResult> ExecuteAsync(
        CommandRequest request,
        CommandExecutionOptions options,
        CancellationToken cancellationToken);
}
