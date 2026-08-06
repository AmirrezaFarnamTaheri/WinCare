using WinCare.Domain.Commands;

namespace WinCare.Application.Commands;

/// <summary>
/// Contract for a single native command handler.
/// </summary>
public interface ICommandHandler
{
    /// <summary>
    /// Gets the catalog command ID this handler implements.
    /// </summary>
    string CommandId { get; }

    /// <summary>
    /// Executes the command and produces a handler-level outcome.
    /// </summary>
    Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken);
}
