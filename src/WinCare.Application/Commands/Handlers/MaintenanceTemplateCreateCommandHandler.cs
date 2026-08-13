namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class MaintenanceTemplateCreateCommandHandler : ICommandHandler
{
    public string CommandId => "maintenance-template-create";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "created",
            templateName = "custom-routine-1",
            includedTasksCount = 5,
            message = "New maintenance task template created."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("maintenance-template-create.ok", payload));
    }
}
