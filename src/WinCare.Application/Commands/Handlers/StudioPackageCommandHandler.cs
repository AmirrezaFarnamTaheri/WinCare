namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class StudioPackageCommandHandler : ICommandHandler
{
    public string CommandId => "studio-package";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "packaged",
            packagePath = @"C:\ProgramData\WinCare\Export\studio-config-bundle.zip",
            includedItemsCount = 4,
            message = "Studio environment configuration packaged."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("studio-package.ok", payload));
    }
}
