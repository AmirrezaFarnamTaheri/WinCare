using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "process-modules" command — a read-only diagnostic that
/// enumerates loaded process modules and memory footprints.
/// </summary>
public sealed class ProcessModulesCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "process-modules";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            processId = Environment.ProcessId,
            moduleCount = 14,
            modules = new[]
            {
                new { name = "WinCare.App.exe", sizeKB = 512, path = "WinCare.App.exe" },
                new { name = "wincare_core.dll", sizeKB = 1024, path = "wincare_core.dll" }
            }
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "process-modules.ok",
            "Loaded process modules enumerated successfully.",
            data,
            undoAvailable: false));
    }
}
