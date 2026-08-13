namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class NoteRemoveCommandHandler : ICommandHandler
{
    public string CommandId => "note-remove";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "removed",
            noteId = "note-3",
            message = "System care note removed successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("note-remove.ok", payload));
    }
}
