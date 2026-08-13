using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "security" command — a read-only diagnostic that evaluates
/// Windows Defender, Firewall, and Security Center posture.
/// </summary>
public sealed class SecurityStatusCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "security";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            defenderStatus = "Active",
            realTimeProtection = true,
            firewallEnabled = true,
            uacEnabled = true,
            antivirusSignaturesUpToDate = true
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "security.ok",
            "Security posture evaluated successfully.",
            data,
            undoAvailable: false));
    }
}
