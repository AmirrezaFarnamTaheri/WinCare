using System.Text.Json;
using System.Text.Json.Serialization;
using WinCare.Application.Native;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "cleaner-disk-pressure" command — dry-run preview and bounded file cleanup execution.
/// </summary>
public sealed class DiskCleanupCommandHandler : ICommandHandler
{
    private readonly INativeCoreService _native;

    /// <inheritdoc />
    public string CommandId => "cleaner-disk-pressure";

    private static readonly string[] DefaultTargets =
    [
        "%TEMP%",
        @"%LOCALAPPDATA%\Temp",
    ];

    /// <summary>
    /// Canonical paths that this handler is permitted to enumerate and delete within.
    /// </summary>
    private static readonly string[] AllowedBasePaths = DefaultTargets
        .Select(Environment.ExpandEnvironmentVariables)
        .Select(NormalizePath)
        .ToArray();

    /// <summary>
    /// Initializes a new instance of the <see cref="DiskCleanupCommandHandler"/> class.
    /// </summary>
    public DiskCleanupCommandHandler(INativeCoreService native)
        => _native = native ?? throw new ArgumentNullException(nameof(native));

    /// <inheritdoc />
    public async Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool targetsSupplied = request.Parameters.TryGetProperty("targets", out JsonElement targetsEl)
            && targetsEl.ValueKind == JsonValueKind.Array;
        string[] rawTargets = targetsSupplied
            ? targetsEl.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0).ToArray()
            : DefaultTargets;

        List<string> resolvedTargets = [];
        foreach (string rawTarget in rawTargets)
        {
            string resolvedTarget;
            try
            {
                resolvedTarget = NormalizePath(Environment.ExpandEnvironmentVariables(rawTarget));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return CommandHandlerOutcome.Blocked(
                    "disk_cleanup.target_denied",
                    "One or more requested cleanup targets are invalid.");
            }

            if (!IsPathAllowed(resolvedTarget))
            {
                return CommandHandlerOutcome.Blocked(
                    "disk_cleanup.target_denied",
                    "One or more requested cleanup targets are outside the allowed temporary-file roots.");
            }

            if (!Directory.Exists(resolvedTarget))
            {
                if (targetsSupplied)
                {
                    return CommandHandlerOutcome.Blocked(
                        "disk_cleanup.target_missing",
                        "One or more requested cleanup targets do not exist.");
                }

                continue;
            }

            try
            {
                if (IsReparsePoint(resolvedTarget))
                {
                    return CommandHandlerOutcome.Blocked(
                        "disk_cleanup.target_reparse_denied",
                        "Cleanup targets cannot be symbolic links, junctions, mount points, or other reparse points.");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return CommandHandlerOutcome.Blocked(
                    "disk_cleanup.target_unavailable",
                    "One or more requested cleanup targets could not be inspected safely.");
            }

            resolvedTargets.Add(resolvedTarget);
        }

        ulong bytesBefore;
        try
        {
            bytesBefore = await MeasureTargetsAsync(resolvedTargets, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OverflowException)
        {
            return CommandHandlerOutcome.Failed(
                "disk_cleanup.measurement_failed",
                "Cleanup space could not be measured reliably, so no changes were applied.");
        }

        if (!request.Apply)
        {
            double estimatedMb = bytesBefore / (1024.0 * 1024.0);
            var preview = new DiskCleanupPreviewRecord(true, [.. resolvedTargets], bytesBefore);
            string json = JsonSerializer.Serialize(preview, DiskCleanupJsonContext.Default.DiskCleanupPreviewRecord);
            using JsonDocument doc = JsonDocument.Parse(json);

            return CommandHandlerOutcome.Succeeded(
                "disk_cleanup.preview",
                $"Preview: ~{estimatedMb:F1} MB across {resolvedTargets.Count} location(s)",
                doc.RootElement.Clone(),
                undoAvailable: false);
        }

        int deletedCount = 0;
        int skippedCount = 0;
        foreach (string dir in resolvedTargets)
        {
            (int deleted, int skipped) = DeleteFilesWithoutFollowingReparsePoints(dir, cancellationToken);
            deletedCount += deleted;
            skippedCount += skipped;
        }

        ulong bytesAfter;
        try
        {
            bytesAfter = await MeasureTargetsAsync(resolvedTargets, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OverflowException)
        {
            return CommandHandlerOutcome.Failed(
                "disk_cleanup.post_measurement_failed",
                $"Cleanup removed {deletedCount} file(s), but freed space could not be measured reliably.");
        }

        ulong freedBytes = bytesBefore > bytesAfter ? bytesBefore - bytesAfter : 0;
        double freedMb = freedBytes / (1024.0 * 1024.0);

        var resultRecord = new DiskCleanupResultRecord(freedBytes, deletedCount, skippedCount);
        string resultJson = JsonSerializer.Serialize(resultRecord, DiskCleanupJsonContext.Default.DiskCleanupResultRecord);
        using JsonDocument resultDoc = JsonDocument.Parse(resultJson);

        return CommandHandlerOutcome.Succeeded(
            "disk_cleanup.applied",
            $"Freed {freedMb:F1} MB ({deletedCount} file(s) deleted, {skippedCount} skipped)",
            resultDoc.RootElement.Clone(),
            undoAvailable: false);
    }

    private async Task<ulong> MeasureTargetsAsync(
        IReadOnlyList<string> targets,
        CancellationToken cancellationToken)
    {
        ulong total = 0;
        foreach (string target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ulong size = await _native.GetDirectorySizeAsync(target, cancellationToken).ConfigureAwait(false);
            total = checked(total + size);
        }

        return total;
    }

    private static (int Deleted, int Skipped) DeleteFilesWithoutFollowingReparsePoints(
        string root,
        CancellationToken cancellationToken)
    {
        int deleted = 0;
        int skipped = 0;
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(directory);
            }
            catch (IOException)
            {
                skipped++;
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                skipped++;
                continue;
            }

            foreach (string entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (IOException)
                {
                    skipped++;
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    skipped++;
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    skipped++;
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }

                try
                {
                    File.Delete(entry);
                    deleted++;
                }
                catch (IOException)
                {
                    skipped++;
                }
                catch (UnauthorizedAccessException)
                {
                    skipped++;
                }
            }
        }

        return (deleted, skipped);
    }

    private static bool IsPathAllowed(string resolvedPath)
    {
        string normalized = NormalizePath(resolvedPath);
        return AllowedBasePaths.Any(allowed =>
            normalized.Equals(allowed, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(
                allowed + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

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
