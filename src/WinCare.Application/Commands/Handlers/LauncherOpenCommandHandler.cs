namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class LauncherOpenCommandHandler : ICommandHandler
{
    public string CommandId => "launcher-open";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "launched",
            targetApp = request.Parameters.TryGetValue("appId", out var app) ? app : "system-default",
            message = "Application launcher requested app execution."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("launcher-open.ok", payload));
    }
}
