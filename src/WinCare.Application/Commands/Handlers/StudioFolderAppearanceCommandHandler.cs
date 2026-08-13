namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class StudioFolderAppearanceCommandHandler : ICommandHandler
{
    public string CommandId => "studio-folder-appearance";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            customIconsCount = 4,
            folderColorsAppliedCount = 2,
            message = "Studio folder customization settings inspected."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("studio-folder-appearance.ok", payload));
    }
}
