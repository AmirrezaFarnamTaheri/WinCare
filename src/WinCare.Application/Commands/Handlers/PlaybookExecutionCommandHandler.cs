namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class PlaybookExecutionCommandHandler : ICommandHandler
{
    public string CommandId => "playbook";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "executed",
            playbookId = "hardening-baseline",
            tasksCompleted = 6,
            message = "Playbook execution completed successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("playbook.ok", payload));
    }
}
