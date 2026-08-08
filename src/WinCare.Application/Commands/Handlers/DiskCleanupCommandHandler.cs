using System.Text.Json;
using System.Text.Json.Serialization;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Native;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "cleaner-disk-pressure" command — dry-run preview and file cleanup execution.
/// </summary>
public sealed class DiskCleanupCommandHandler : ICommandHandler
{
    private readonly NativeCoreService _native;

    /// <inheritdoc />
    public string CommandId => "cleaner-disk-pressure";

    private static readonly string[] DefaultTargets =
    [
        "%TEMP%",
        @"%LOCALAPPDATA%\Temp",
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskCleanupCommandHandler"/> class.
    /// </summary>
    public DiskCleanupCommandHandler(NativeCoreService native)
        => _native = native ?? throw new ArgumentNullException(nameof(native));

    /// <inheritdoc />
    public async Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string[] rawTargets = request.Parameters.TryGetProperty("targets", out JsonElement targetsEl)
            && targetsEl.ValueKind == JsonValueKind.Array
            ? targetsEl.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToArray()
            : DefaultTargets;

        string[] resolvedTargets = rawTargets
            .Select(Environment.ExpandEnvironmentVariables)
            .Where(Directory.Exists)
            .ToArray();

        if (!request.Apply)
        {
            ulong estimated = 0;
            foreach (string dir in resolvedTargets)
            {
                try
                {
                    estimated += await _native.GetDirectorySizeAsync(dir, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Skip inaccessible directory in preview
                }
            }

            double estimatedMb = estimated / (1024.0 * 1024.0);
            var preview = new DiskCleanupPreviewRecord(true, resolvedTargets, estimated);
            string json = JsonSerializer.Serialize(preview, DiskCleanupJsonContext.Default.DiskCleanupPreviewRecord);
            using JsonDocument doc = JsonDocument.Parse(json);

            return CommandHandlerOutcome.Succeeded(
                "disk_cleanup.preview",
                $"Preview: ~{estimatedMb:F1} MB across {resolvedTargets.Length} location(s)",
                doc.RootElement.Clone(),
                undoAvailable: false);
        }

        ulong bytesBefore = 0;
        foreach (string dir in resolvedTargets)
        {
            try
            {
                bytesBefore += await _native.GetDirectorySizeAsync(dir, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }

        int deletedCount = 0;
        int errorCount = 0;

        foreach (string dir in resolvedTargets)
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                    catch (IOException)
                    {
                        errorCount++;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        errorCount++;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                errorCount++;
            }
        }

        ulong bytesAfter = 0;
        foreach (string dir in resolvedTargets)
        {
            try
            {
                bytesAfter += await _native.GetDirectorySizeAsync(dir, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }

        ulong freedBytes = bytesBefore > bytesAfter ? bytesBefore - bytesAfter : 0;
        double freedMb = freedBytes / (1024.0 * 1024.0);

        var resultRecord = new DiskCleanupResultRecord(freedBytes, deletedCount, errorCount);
        string resultJson = JsonSerializer.Serialize(resultRecord, DiskCleanupJsonContext.Default.DiskCleanupResultRecord);
        using JsonDocument resultDoc = JsonDocument.Parse(resultJson);

        return CommandHandlerOutcome.Succeeded(
            "disk_cleanup.applied",
            $"Freed {freedMb:F1} MB ({deletedCount} file(s) deleted, {errorCount} skipped)",
            resultDoc.RootElement.Clone(),
            undoAvailable: false);
    }

    /// <summary>
    /// Represents disk cleanup preview record.
    /// </summary>
    public sealed record DiskCleanupPreviewRecord(bool Preview, string[] Candidates, ulong EstimatedFreeBytes);

    /// <summary>
    /// Represents disk cleanup execution result record.
    /// </summary>
    public sealed record DiskCleanupResultRecord(ulong FreedBytes, int DeletedFilesCount, int SkippedFilesCount);
}

[JsonSerializable(typeof(DiskCleanupCommandHandler.DiskCleanupPreviewRecord))]
[JsonSerializable(typeof(DiskCleanupCommandHandler.DiskCleanupResultRecord))]
internal sealed partial class DiskCleanupJsonContext : JsonSerializerContext
{
}
