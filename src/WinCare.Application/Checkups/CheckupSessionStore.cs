using System.Text.Json;
using WinCare.Application.Storage;

namespace WinCare.Application.Checkups;

/// <summary>
/// A single bounded, provenance-preserving checkup run. These are probe outcomes,
/// not claims that an antivirus, disk or Windows service is healthy.
/// </summary>
public sealed record CheckupAreaSnapshot(string CommandId, string State);

public sealed record CheckupSessionSnapshot(
    int SchemaVersion,
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Status,
    IReadOnlyList<CheckupAreaSnapshot> Areas);

/// <summary>
/// Atomic, process-lifetime last-checkup journal. Unlike unrelated Activity entries,
/// this record identifies the four results belonging to one actual checkup run.
/// </summary>
public sealed class CheckupSessionStore
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumSnapshotBytes = 64 * 1024;
    public static readonly string[] ProbeIds = ["system", "storage", "security", "wua-search"];
    private static readonly IReadOnlySet<string> KnownStates =
        new HashSet<string>(StringComparer.Ordinal)
        { "Pending", "Running", "Checked", "NeedsReview", "Cancelled", "Interrupted" };

    /// <summary>
    /// Successful data collection is not the same as a healthy device. Carry visible
    /// warning and critical findings into Home's durable single-session summary.
    /// </summary>
    public static string ClassifyArea(string probeState, string? assessmentBrush) =>
        probeState == "Checked" &&
        assessmentBrush is ("WarningBrush" or "DangerBrush")
            ? "NeedsReview" : probeState;

    private readonly string _path;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private CheckupSessionSnapshot? _latest;

    public CheckupSessionStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppDataRoot.Current, "checkup-session.json");
    }

    /// <summary>Fired only after a snapshot has been committed successfully.</summary>
    public event EventHandler? Changed;

    public CheckupSessionSnapshot? ReadLatest()
    {
        if (_latest is not null) return _latest;
        try
        {
            if (!File.Exists(_path)) return null;
            using FileStream stream = new(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumSnapshotBytes) return null;
            CheckupSessionSnapshot? restored = JsonSerializer.Deserialize<CheckupSessionSnapshot>(stream);
            if (!IsValid(restored)) return null;
            // A restarted application cannot still have the old in-flight probes.
            return restored!.Status == "Running"
                ? restored with
                {
                    Status = "Interrupted",
                    CompletedAt = DateTimeOffset.UtcNow,
                    Areas = restored.Areas.Select(area => area.State == "Running"
                        ? area with { State = "Interrupted" } : area).ToArray()
                }
                : restored;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"[CheckupSessionStore] Read failed: {ex}");
            return null;
        }
    }

    public async Task SaveAsync(CheckupSessionSnapshot session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!IsValid(session))
            throw new InvalidDataException("Checkup snapshot is malformed or has unknown probes.");

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            string directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidDataException("The checkup snapshot path has no parent directory.");
            Directory.CreateDirectory(directory);
            temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (FileStream stream = new(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, session, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
                if (stream.Length > MaximumSnapshotBytes)
                    throw new InvalidDataException("Checkup snapshot exceeded the size limit.");
            }

            File.Move(temporary, _path, overwrite: true);
            temporary = null;
            _latest = session;
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); } catch (IOException) { }
            }
            _writeGate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static bool IsValid(CheckupSessionSnapshot? session)
    {
        if (session is null || session.SchemaVersion != CurrentSchemaVersion ||
            session.Id == Guid.Empty || session.StartedAt == default ||
            session.Status is not ("Running" or "Completed" or "Incomplete" or "Cancelled" or "Interrupted") ||
            session.Areas is null || session.Areas.Count != ProbeIds.Length)
            return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (CheckupAreaSnapshot area in session.Areas)
        {
            if (area is null || !ProbeIds.Contains(area.CommandId, StringComparer.Ordinal) ||
                !KnownStates.Contains(area.State) || !seen.Add(area.CommandId))
                return false;
        }
        return true;
    }
}
