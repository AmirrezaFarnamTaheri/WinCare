namespace WinCare.Application.Commands;

using System.Threading;
using System.Threading.Tasks;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

/// <summary>
/// Autonomous subsystem command executor contract for WinCare 4.0 micro-kernel architecture.
/// Replaces monolithic executor dispatching by routing domain commands to specialized subsystem handlers.
/// </summary>
public interface ISubsystemCommandExecutor
{
    /// <summary>
    /// Gets the unique subsystem domain identifier (e.g., "Storage", "Servicing", "Security", "Remediation").
    /// </summary>
    string Subsystem { get; }

    /// <summary>
    /// Determines whether this subsystem executor can handle the specified command ID.
    /// </summary>
    bool CanHandle(string commandId);

    /// <summary>
    /// Executes the command request asynchronously within this subsystem's boundary.
    /// </summary>
    Task<CommandHandlerOutcome> ExecuteAsync(
        CommandDefinition definition, 
        CommandRequest request, 
        CancellationToken cancellationToken);

    /// <summary>
    /// Generates a read-only execution preview and impact receipt before mutation approval.
    /// </summary>
    CommandPreview PlanPreview(
        CommandDefinition definition, 
        SubsystemCommandParameters parameters);
}
