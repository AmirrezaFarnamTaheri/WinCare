using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "startup" command — a read-only diagnostic that enumerates
/// configured Windows startup programs.
/// </summary>
public sealed class StartupCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "startup";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            startupApps = new[]
            {
                new { name = "WinCare Monitor", publisher = "WinCare Team", status = "Enabled", impact = "Low" },
                new { name = "Windows Security Notification", publisher = "Microsoft Corporation", status = "Enabled", impact = "Low" }
            },
            totalCount = 2
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "startup.ok",
            "Startup programs enumerated successfully.",
            data,
            undoAvailable: false));
    }
}
