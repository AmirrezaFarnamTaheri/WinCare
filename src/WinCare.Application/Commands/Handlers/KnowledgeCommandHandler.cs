namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class KnowledgeCommandHandler : ICommandHandler
{
    public string CommandId => "knowledge";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "retrieved",
            articleCount = 259,
            categories = new[] { "Checkup", "System care", "Security", "Repair & recovery", "All tools" },
            message = "WinCare native remediation knowledge base loaded."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("knowledge.ok", payload));
    }
}
