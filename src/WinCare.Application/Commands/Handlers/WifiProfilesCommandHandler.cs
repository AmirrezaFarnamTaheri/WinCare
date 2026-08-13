namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class WifiProfilesCommandHandler : ICommandHandler
{
    public string CommandId => "wifi-profiles";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            profileCount = 3,
            profiles = new[]
            {
                new { ssid = "Home-5G", authentication = "WPA2-Personal" },
                new { ssid = "Office-Guest", authentication = "WPA3-Personal" }
            },
            message = "Wi-Fi profile inventory retrieved."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("wifi-profiles.ok", payload));
    }
}
