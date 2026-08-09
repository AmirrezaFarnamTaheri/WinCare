using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the read-only "cleanup-targets" command by discovering Windows event-log targets.
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

        if (request.Apply)
        {
            return CommandHandlerOutcome.Blocked(
                "log_cleanup.readonly",
                "cleanup-targets is read-only and cannot clear Windows event logs.");
        }

        LogInfoRecord[] logInfo;
        try
        {
            logInfo = await Task.Run(() => EventLog.GetEventLogs()
                .Select(log =>
                {
                    using (log)
                    {
                        return new LogInfoRecord(log.Log, log.Entries.Count);
                    }
                })
                .ToArray(), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return CommandHandlerOutcome.Failed(
                "log_cleanup.enumeration_failed",
                "Windows event logs could not be enumerated reliably.");
        }

        var preview = new LogCleanupPreviewRecord(true, logInfo);
        string json = JsonSerializer.Serialize(preview, LogCleanupJsonContext.Default.LogCleanupPreviewRecord);
        using JsonDocument doc = JsonDocument.Parse(json);

        return CommandHandlerOutcome.Succeeded(
            "log_cleanup.preview",
            $"Preview: {logInfo.Length} event log(s) found.",
            doc.RootElement.Clone(),
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
}

[JsonSerializable(typeof(LogCleanupCommandHandler.LogCleanupPreviewRecord))]
internal sealed partial class LogCleanupJsonContext : JsonSerializerContext
{
}
