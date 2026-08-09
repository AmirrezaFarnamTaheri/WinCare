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

        var records = new List<NetworkRecord>();
        foreach (NetworkInterface n in ifaces)
        {
            long speed;
            try
            {
                speed = n.Speed;
            }
            catch (Exception)
            {
                // Some virtual or tunnel adapters throw on Speed — report as unknown.
                speed = -1;
            }
            records.Add(new NetworkRecord(
                n.Name,
                n.NetworkInterfaceType.ToString(),
                n.OperationalStatus.ToString(),
                speed));
        }

        string json = JsonSerializer.Serialize(records.ToArray(), NetworkStatusJsonContext.Default.NetworkRecordArray);
        using JsonDocument doc = JsonDocument.Parse(json);

        return CommandHandlerOutcome.Succeeded(
            "network.ok",
            $"Found {records.Count} network adapter(s).",
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
