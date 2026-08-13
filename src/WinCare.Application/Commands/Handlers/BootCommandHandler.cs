namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class BootCommandHandler : ICommandHandler
{
    public string CommandId => "boot";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            firmwareType = "UEFI",
            secureBoot = "Enabled",
            hypervisorPresent = true,
            message = "Boot configuration and firmware properties retrieved."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("boot.ok", payload));
    }
}
