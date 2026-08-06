using WinCare.CommandCatalog;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Exposes the built-in preset catalog.
/// </summary>
public sealed class PresetsCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "presets";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<PresetDefinition> presets = RemediationCatalog.LoadPresets();
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "presets.loaded",
            $"Loaded {presets.Count} built-in presets.",
            RemediationCatalog.SerializePresets()));
    }
}
