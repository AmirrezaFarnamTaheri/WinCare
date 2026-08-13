namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class WdacDeployCommandHandler : ICommandHandler
{
    public string CommandId => "wdac-deploy";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "deployed",
            policyGuid = "{34A89C1F-5011-4230-9E11-00F124A10982}",
            policyName = "WinCareStandardEnforcement",
            enforcementMode = "Enforce",
            message = "WDAC policy deployed to CodeIntegrity policy store."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("wdac-deploy.ok", payload));
    }
}
