using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "cleanup-targets" command — dry-run preview and event log cleanup execution.
/// </summary>
public sealed class LogCleanupCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "cleanup-targets";

    /// <inheritdoc />
    public async Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!request.Apply)
        {
            LogInfoRecord[] logInfo = await Task.Run(() =>
            {
                try
                {
                    return EventLog.GetEventLogs()
                        .Select(l => new LogInfoRecord(l.Log, l.Entries.Count))
                        .ToArray();
                }
                catch
                {
                    return Array.Empty<LogInfoRecord>();
                }
            }, cancellationToken).ConfigureAwait(false);

            var preview = new LogCleanupPreviewRecord(true, logInfo);
            string json = JsonSerializer.Serialize(preview, LogCleanupJsonContext.Default.LogCleanupPreviewRecord);
            using JsonDocument doc = JsonDocument.Parse(json);

            return CommandHandlerOutcome.Succeeded(
                "log_cleanup.preview",
                $"Preview: {logInfo.Length} event log(s) found.",
                doc.RootElement.Clone(),
                undoAvailable: false);
        }

        string[] selectedLogs = request.Parameters.TryGetProperty("logs", out JsonElement logsEl)
            && logsEl.ValueKind == JsonValueKind.Array
            ? logsEl.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToArray()
            : ["Application", "System"];

        int clearedCount = 0;
        int errorCount = 0;

        await Task.Run(() =>
        {
            foreach (string logName in selectedLogs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using EventLog log = new(logName);
                    log.Clear();
                    clearedCount++;
                }
                catch
                {
                    errorCount++;
                }
            }
        }, cancellationToken).ConfigureAwait(false);

        var resultRecord = new LogCleanupResultRecord(clearedCount, errorCount);
        string resultJson = JsonSerializer.Serialize(resultRecord, LogCleanupJsonContext.Default.LogCleanupResultRecord);
        using JsonDocument resultDoc = JsonDocument.Parse(resultJson);

        return CommandHandlerOutcome.Succeeded(
            "log_cleanup.applied",
            $"Cleared {clearedCount} log(s), {errorCount} skipped.",
            resultDoc.RootElement.Clone(),
            undoAvailable: false);
    }

    /// <summary>
    /// Represents single event log info record.
    /// </summary>
    public sealed record LogInfoRecord(string LogName, int EntryCount);

    /// <summary>
    /// Represents log cleanup preview record.
    /// </summary>
    public sealed record LogCleanupPreviewRecord(bool Preview, LogInfoRecord[] Logs);

    /// <summary>
    /// Represents log cleanup execution result record.
    /// </summary>
    public sealed record LogCleanupResultRecord(int ClearedLogsCount, int SkippedLogsCount);
}

[JsonSerializable(typeof(LogCleanupCommandHandler.LogCleanupPreviewRecord))]
[JsonSerializable(typeof(LogCleanupCommandHandler.LogCleanupResultRecord))]
internal sealed partial class LogCleanupJsonContext : JsonSerializerContext
{
}
