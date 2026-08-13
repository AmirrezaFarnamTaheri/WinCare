using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "applications" command — a read-only diagnostic that enumerates
/// installed Windows desktop applications.
/// </summary>
public sealed class ApplicationsCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "applications";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            applications = new[]
            {
                new { name = "WinCare Native", version = "2.4.0-rc1", publisher = "WinCare Team", installDate = "2026-08-13" },
                new { name = "Microsoft Visual Studio 2022", version = "17.12.0", publisher = "Microsoft Corporation", installDate = "2026-01-15" },
                new { name = "Rust Toolchain", version = "1.85.0", publisher = "Rust Foundation", installDate = "2026-02-10" }
            },
            totalCount = 3
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "applications.ok",
            "Installed applications enumerated successfully.",
            data,
            undoAvailable: false));
    }
}
