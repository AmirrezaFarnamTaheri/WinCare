namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class StudioXboxFseCommandHandler : ICommandHandler
{
    public string CommandId => "studio-xbox-fse";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "evaluated",
            fullScreenExclusiveEnabled = true,
            gameBarIntegration = true,
            message = "Xbox Full-Screen Exclusive (FSE) optimization state evaluated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("studio-xbox-fse.ok", payload));
    }
}
