namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class PlaybooksCommandHandler : ICommandHandler
{
    public string CommandId => "playbooks";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            availablePlaybooksCount = 4,
            playbooks = new[]
            {
                new { id = "hardening-baseline", name = "Enterprise Hardening Baseline" },
                new { id = "performance-tune", name = "Gaming Performance Optimization" }
            },
            message = "Remediation playbooks enumerated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("playbooks.ok", payload));
    }
}
