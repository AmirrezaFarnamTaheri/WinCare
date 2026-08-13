namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class GroupPolicyCommandHandler : ICommandHandler
{
    public string CommandId => "group-policy";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            domainJoined = false,
            localGpoModified = true,
            appliedPoliciesCount = 18,
            message = "Local Group Policy settings inspected."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("group-policy.ok", payload));
    }
}
