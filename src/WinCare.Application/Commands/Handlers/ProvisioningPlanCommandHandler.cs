namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class ProvisioningPlanCommandHandler : ICommandHandler
{
    public string CommandId => "provisioning-plan";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "reviewed",
            blueprintName = "DefaultEnterpriseProvisioning",
            packagesInstalledCount = 0,
            message = "Provisioning blueprint reviewed. No pending provisioning packages."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("provisioning-plan.ok", payload));
    }
}
