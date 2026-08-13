namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class NoteSaveCommandHandler : ICommandHandler
{
    public string CommandId => "note-save";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "saved",
            noteId = "note-3",
            title = "New system note",
            updatedAt = "2026-08-13T22:13:00Z",
            message = "System care note saved successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("note-save.ok", payload));
    }
}
