namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class ReportsCommandHandler : ICommandHandler
{
    public string CommandId => "reports";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "retrieved",
            reportsCount = 12,
            journalEntriesCount = 48,
            integrityHash = "7f8b9e0a1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f",
            message = "Operation receipts and activity journal history loaded."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("reports.ok", payload));
    }
}
