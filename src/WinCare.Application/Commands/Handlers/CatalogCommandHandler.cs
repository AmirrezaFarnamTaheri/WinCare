using WinCare.CommandCatalog;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Exposes the built-in remediation catalog.
/// </summary>
public sealed class CatalogCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "catalog";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<RemediationRule> rules = RemediationCatalog.LoadRules();
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "catalog.loaded",
            $"Loaded {rules.Count} built-in configuration rules.",
            RemediationCatalog.SerializeRules()));
    }
}
