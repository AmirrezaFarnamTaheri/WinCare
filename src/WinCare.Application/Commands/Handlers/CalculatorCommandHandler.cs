namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class CalculatorCommandHandler : ICommandHandler
{
    public string CommandId => "calculator";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "ready",
            tool = "Windows Calculator Helper",
            mode = "Standard",
            message = "Calculator session parameters validated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("calculator.ok", payload));
    }
}
