using System.Text.Json;
using System.Text.Json.Serialization;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "storage" command — enumerates drive health and space.
/// </summary>
public sealed class StorageHealthCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "storage";

    /// <inheritdoc />
    public async Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DriveInfo[] drives = await Task.Run(DriveInfo.GetDrives, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var records = new List<DriveRecord>();
        foreach (DriveInfo d in drives)
        {
            if (!d.IsReady)
            {
                continue;
            }
            try
            {
                records.Add(new DriveRecord(d.Name, d.DriveFormat, d.TotalSize, d.AvailableFreeSpace));
            }
            catch (IOException)
            {
                // Drive became unavailable between IsReady check and property read — skip it.
            }
            catch (UnauthorizedAccessException)
            {
                // No permission to read drive details — skip it.
            }
        }

        string json = JsonSerializer.Serialize(records.ToArray(), StorageHealthJsonContext.Default.DriveRecordArray);
        using JsonDocument doc = JsonDocument.Parse(json);

        return CommandHandlerOutcome.Succeeded(
            "storage.ok",
            $"Found {records.Count} ready drive(s).",
            doc.RootElement.Clone(),
            undoAvailable: false);
    }

    /// <summary>
    /// Represents drive space diagnostics record.
    /// </summary>
    public sealed record DriveRecord(string Name, string Format, long TotalBytes, long FreeBytes);
}

[JsonSerializable(typeof(StorageHealthCommandHandler.DriveRecord[]))]
internal sealed partial class StorageHealthJsonContext : JsonSerializerContext
{
}
