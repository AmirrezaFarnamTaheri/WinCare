namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class StudioLayoutSaveCommandHandler : ICommandHandler
{
    public string CommandId => "studio-layout-save";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "saved",
            profileName = "custom-studio-profile-1",
            displayCount = 2,
            message = "Studio layout profile saved successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("studio-layout-save.ok", payload));
    }
}
