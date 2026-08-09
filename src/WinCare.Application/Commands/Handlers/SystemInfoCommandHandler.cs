using System.Text.Json;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Native;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "system" command — a read-only diagnostic that collects
/// CPU count, memory totals, and OS build via the native wincare_core_sys_info ABI.
/// </summary>
public sealed class SystemInfoCommandHandler : ICommandHandler
{
    private readonly NativeCoreService _native;

    /// <inheritdoc />
    public string CommandId => "system";

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemInfoCommandHandler"/> class.
    /// </summary>
    public SystemInfoCommandHandler(NativeCoreService native)
        => _native = native ?? throw new ArgumentNullException(nameof(native));

    /// <inheritdoc />
    public async Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string json = await _native.GetSystemInfoJsonAsync(cancellationToken)
            .ConfigureAwait(false);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return CommandHandlerOutcome.Failed(
                "system.parse_error",
                $"Native sys-info response could not be parsed: {ex.GetType().Name}");
        }

        using (doc)
        {
            JsonElement data = doc.RootElement.Clone();
            return CommandHandlerOutcome.Succeeded(
                "system.ok",
                "System information collected successfully.",
                data,
                undoAvailable: false);
        }
    }
}
