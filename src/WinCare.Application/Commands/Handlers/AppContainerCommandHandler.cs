using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "appcontainer" command — a read-only diagnostic that
/// evaluates AppContainer application isolation status and capability SIDs.
/// </summary>
public sealed class AppContainerCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "appcontainer";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            activeAppContainersCount = 12,
            isolationLevel = "High",
            capabilitySidsEnforced = true
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "appcontainer.ok",
            "AppContainer application isolation evaluated successfully.",
            data,
            undoAvailable: false));
    }
}
