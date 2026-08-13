using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands;

/// <summary>
/// Thin adapter from stable command IDs to the platform operation executor.
/// </summary>
public sealed class DelegatingCommandHandler : ICommandHandler
{
    private readonly CommandDefinition _definition;
    private readonly ICommandOperationExecutor _executor;

    public DelegatingCommandHandler(CommandDefinition definition, ICommandOperationExecutor executor)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public string CommandId => _definition.Id;

    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken) =>
        _executor.ExecuteAsync(_definition, request, cancellationToken);
}
