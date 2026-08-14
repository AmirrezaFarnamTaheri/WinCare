using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands;

/// <summary>
/// Platform execution boundary for one cataloged WinCare command.
/// Application owns admission and result shaping; infrastructure owns Windows I/O.
/// </summary>
public interface ICommandOperationExecutor
{
    /// <summary>
    /// Executes or previews a catalog definition using the typed request.
    /// Implementations must fail closed when prerequisites or parameters are invalid.
    /// </summary>
    Task<CommandHandlerOutcome> ExecuteAsync(
        CommandDefinition definition,
        CommandRequest request,
        CancellationToken cancellationToken);
}
