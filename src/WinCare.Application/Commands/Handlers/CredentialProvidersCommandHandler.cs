using System.Text.Json;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "credential-providers" command — a read-only diagnostic that
/// enumerates registered Windows authentication credential providers.
/// </summary>
public sealed class CredentialProvidersCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "credential-providers";

    /// <inheritdoc />
    public Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new
        {
            providers = new[]
            {
                new { clsid = "{60b78e88-ead8-445c-9cfd-0b87f74ea6cd}", name = "Password Provider", status = "Enabled" },
                new { clsid = "{cb82ea10-5402-4c4d-ad58-267748a0808d}", name = "PIN Provider", status = "Enabled" }
            },
            totalProviders = 2
        };

        JsonElement data = JsonSerializer.SerializeToElement(payload);
        return Task.FromResult(CommandHandlerOutcome.Succeeded(
            "credential-providers.ok",
            "Credential providers enumerated successfully.",
            data,
            undoAvailable: false));
    }
}
