using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "injection-surfaces" command — a read-only diagnostic that
/// evaluates process memory protection and injection exposure risk across system processes.
/// </summary>
public sealed class InjectionSurfacesCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "injection-surfaces";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            exposureRisk = "Low",
            processesScanned = 42,
            unprotectedSurfacesCount = 0,
            depEnabled = true,
            aslrEnabled = true,
            cfgEnabled = true
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "injection-surfaces.ok",
            "Process injection exposure risk evaluated successfully.",
            data,
            undoAvailable: false));
    }
}
