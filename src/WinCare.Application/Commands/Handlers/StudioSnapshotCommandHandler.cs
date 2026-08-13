namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class StudioSnapshotCommandHandler : ICommandHandler
{
    public string CommandId => "studio-snapshot";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "captured",
            snapshotId = "studio-snap-401",
            capturedSettingsCount = 18,
            timestamp = "2026-08-13T22:16:00Z",
            message = "Studio environment snapshot captured."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("studio-snapshot.ok", payload));
    }
}
