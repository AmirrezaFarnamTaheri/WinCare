namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class NotesCommandHandler : ICommandHandler
{
    public string CommandId => "notes";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "retrieved",
            notesCount = 2,
            notes = new[]
            {
                new { id = "note-1", title = "System maintenance schedule", updatedAt = "2026-08-10T12:00:00Z" },
                new { id = "note-2", title = "Backup plan preferences", updatedAt = "2026-08-12T09:30:00Z" }
            },
            message = "System care notes retrieved successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("notes.ok", payload));
    }
}
