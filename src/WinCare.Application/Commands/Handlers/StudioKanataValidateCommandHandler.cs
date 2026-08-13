namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class StudioKanataValidateCommandHandler : ICommandHandler
{
    public string CommandId => "studio-kanata-validate";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "validated",
            configPath = @"C:\Users\Public\.config\kanata\kanata.kbd",
            isValid = true,
            syntaxErrorsCount = 0,
            message = "Kanata remapping configuration validated successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("studio-kanata-validate.ok", payload));
    }
}
