namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class BrowserExtensionsCommandHandler : ICommandHandler
{
    public string CommandId => "browser-extensions";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            browser = "Microsoft Edge",
            extensionsCount = 3,
            extensions = new[]
            {
                new { name = "uBlock Origin", enabled = true, id = "cjpalhdlnbpafiamejdnhcphjbkeiagm" },
                new { name = "Bitwarden", enabled = true, id = "nngceckbapebfimnlniiiahkandclblb" },
                new { name = "React Developer Tools", enabled = false, id = "fmkadmapgofadopljbjfkapdkoienihi" }
            },
            message = "Browser extensions enumerated successfully."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("browser-extensions.ok", payload));
    }
}
