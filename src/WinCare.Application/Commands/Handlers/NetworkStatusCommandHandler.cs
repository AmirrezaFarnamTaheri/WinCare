using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "network" command — enumerates network adapters and status.
/// </summary>
public sealed class NetworkStatusCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "network";

    /// <inheritdoc />
    public async Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NetworkInterface[] ifaces = await Task.Run(
            NetworkInterface.GetAllNetworkInterfaces, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var records = ifaces.Select(n => new NetworkRecord(
            n.Name,
            n.NetworkInterfaceType.ToString(),
            n.OperationalStatus.ToString(),
            n.Speed))
            .ToArray();

        string json = JsonSerializer.Serialize(records, NetworkStatusJsonContext.Default.NetworkRecordArray);
        using JsonDocument doc = JsonDocument.Parse(json);

        return CommandHandlerOutcome.Succeeded(
            "network.ok",
            $"Found {records.Length} network adapter(s).",
            doc.RootElement.Clone(),
            undoAvailable: false);
    }

    /// <summary>
    /// Represents network adapter diagnostics record.
    /// </summary>
    public sealed record NetworkRecord(string Name, string Type, string Status, long SpeedBitsPerSec);
}

[JsonSerializable(typeof(NetworkStatusCommandHandler.NetworkRecord[]))]
internal sealed partial class NetworkStatusJsonContext : JsonSerializerContext
{
}
