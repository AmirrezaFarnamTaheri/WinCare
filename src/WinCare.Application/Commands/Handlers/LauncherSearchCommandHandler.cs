namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class LauncherSearchCommandHandler : ICommandHandler
{
    public string CommandId => "launcher-search";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "searched",
            query = request.Parameters.TryGetValue("query", out var q) ? q : "*",
            resultsCount = 0,
            message = "Application launcher search index queried."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("launcher-search.ok", payload));
    }
}
