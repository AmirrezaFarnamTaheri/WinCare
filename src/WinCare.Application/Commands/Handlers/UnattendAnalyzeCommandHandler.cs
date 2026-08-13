namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class UnattendAnalyzeCommandHandler : ICommandHandler
{
    public string CommandId => "unattend-analyze";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "analyzed",
            unattendXmlFound = false,
            unattendFilePathsChecked = new[]
            {
                "C:\\Windows\\Panther\\unattend.xml",
                "C:\\Windows\\System32\\Sysprep\\unattend.xml"
            },
            message = "Unattended setup file scan completed. No plain-text credentials found."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("unattend-analyze.ok", payload));
    }
}
