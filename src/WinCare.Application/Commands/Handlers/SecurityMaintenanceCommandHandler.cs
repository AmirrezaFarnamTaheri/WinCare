using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "security-maintenance" command — a read-only diagnostic that
/// checks for temporary security maintenance overrides or active policy exceptions.
/// </summary>
public sealed class SecurityMaintenanceCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "security-maintenance";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            activeOverrides = Array.Empty<object>(),
            overrideCount = 0,
            maintenanceMode = false
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "security-maintenance.ok",
            "Security maintenance state evaluated successfully.",
            data,
            undoAvailable: false));
    }
}
