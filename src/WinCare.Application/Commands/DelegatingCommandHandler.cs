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

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegatingCommandHandler"/> class.
    /// </summary>
    /// <param name="definition">The catalog command definition.</param>
    /// <param name="executor">The operation executor implementation.</param>
    public DelegatingCommandHandler(CommandDefinition definition, ICommandOperationExecutor executor)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    /// <inheritdoc />
    public string CommandId => _definition.Id;

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        if (!CommandRequestCompatibility.TryNormalize(request, out CommandRequest normalized, out string? error))
        {
            return Task.FromResult(CommandHandlerOutcome.Blocked(
                "command.parameters_invalid",
                error ?? "Command parameters are invalid."));
        }

        return _executor.ExecuteAsync(_definition, normalized, cancellationToken);
    }
}
