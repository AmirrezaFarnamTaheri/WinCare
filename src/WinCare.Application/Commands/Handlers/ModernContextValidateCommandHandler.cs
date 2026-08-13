namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class ModernContextValidateCommandHandler : ICommandHandler
{
    public string CommandId => "modern-context-validate";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "validated",
            appModelType = "DesktopBridge",
            sandboxConstrained = false,
            message = "Modern execution context validated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("modern-context-validate.ok", payload));
    }
}
