namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class ToolkitMsiCommandHandler : ICommandHandler
{
    public string CommandId => "toolkit-msi";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            msiDatabasePath = @"C:\Windows\Installer\example.msi",
            productCode = "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}",
            productName = "WinCare Native Core Component",
            message = "MSI installer package database inspected."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("toolkit-msi.ok", payload));
    }
}
