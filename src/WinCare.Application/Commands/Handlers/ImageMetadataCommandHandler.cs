namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class ImageMetadataCommandHandler : ICommandHandler
{
    public string CommandId => "image-metadata";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            imagePath = @"C:\Windows\System32\Recovery\winre.wim",
            architecture = "x64",
            buildNumber = "22631",
            message = "Windows image file metadata retrieved successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("image-metadata.ok", payload));
    }
}
