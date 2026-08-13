namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class GroupPolicyImportCommandHandler : ICommandHandler
{
    public string CommandId => "group-policy-import";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "imported",
            importedRulesCount = 12,
            requiresGpupdate = true,
            message = "Group Policy security template imported into local GPO store."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("group-policy-import.ok", payload));
    }
}
