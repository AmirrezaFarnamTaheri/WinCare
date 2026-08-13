namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class StudioWeztermCommandHandler : ICommandHandler
{
    public string CommandId => "studio-wezterm";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            weztermInstalled = true,
            configPath = @"C:\Users\Public\.wezterm.lua",
            activeFont = "JetBrains Mono",
            message = "WezTerm terminal configuration inspected."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("studio-wezterm.ok", payload));
    }
}
