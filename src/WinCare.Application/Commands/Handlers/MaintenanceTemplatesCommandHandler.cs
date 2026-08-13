namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class MaintenanceTemplatesCommandHandler : ICommandHandler
{
    public string CommandId => "maintenance-templates";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "retrieved",
            templatesCount = 3,
            templates = new[]
            {
                new { name = "weekly-quick-clean", tasksCount = 4 },
                new { name = "monthly-deep-tune", tasksCount = 12 },
                new { name = "security-audit-routine", tasksCount = 8 }
            },
            message = "Automated maintenance task templates retrieved."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("maintenance-templates.ok", payload));
    }
}
