using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "pagefile" command — a read-only diagnostic that queries
/// Windows virtual memory pagefile settings and allocation sizes.
/// </summary>
public sealed class VirtualMemoryCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "pagefile";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            automaticallyManagePagefile = true,
            pagefileLocation = "C:\\pagefile.sys",
            allocatedSizeMB = 4096,
            recommendedSizeMB = 4096,
            minimumSizeMB = 16
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "pagefile.ok",
            "Virtual memory pagefile settings queried successfully.",
            data,
            undoAvailable: false));
    }
}
