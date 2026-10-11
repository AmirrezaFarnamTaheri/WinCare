using System.Globalization;
using System.Text.Json;

namespace WinCare.Application.Downloads;

/// <summary>A bounded, privacy-conscious projection of native persisted download jobs.</summary>
public sealed record DownloadJobRow(
    string Id,
    string Name,
    string Host,
    string Destination,
    string State,
    string StateLabel,
    string TransferLabel,
    double ProgressPercent,
    bool IsIndeterminate,
    bool ShowProgress,
    bool CanStart,
    bool CanPause,
    bool CanResume,
    bool CanCancel,
    bool CanRemove,
    DateTimeOffset CreatedAt)
{
    public string AccessibleName => $"{Name}, {Host}, {StateLabel}, {TransferLabel}";
    public string StartAccessibleName => $"Start download {Name}";
    public string PauseAccessibleName => $"Pause download {Name}";
    public string ResumeAccessibleName => $"Resume download {Name}";
    public string CancelAccessibleName => $"Cancel download {Name}";
    public string RemoveAccessibleName => $"Remove download record for {Name}";
    public string ActionsAccessibleName => $"More actions for {Name}";
    public string FolderAccessibleName => $"Browse the folder containing {Name}";
}

/// <summary>Decodes job state without displaying secret-bearing URLs, headers or query strings.</summary>
public static class DownloadJobProjection
{
    private const int MaxVisibleJobs = 250;

    public static IReadOnlyList<DownloadJobRow> Project(
        JsonElement? payload, IReadOnlyCollection<string>? activeIds = null)
    {
        HashSet<string>? active = activeIds is null
            ? null : new HashSet<string>(activeIds, StringComparer.OrdinalIgnoreCase);
        if (payload is not JsonElement data || data.ValueKind != JsonValueKind.Array) return [];
        var jobs = new List<DownloadJobRow>();
        foreach (JsonElement item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            string id = Text(item, "id");
            if (id.Length is 0 or > 128) continue;
            string destination = Text(item, "destination");
            // Persisted Windows paths may be inspected in cross-platform tests.
            // Never expose an entire home-directory path when the host's native
            // Path.GetFileName separator rules differ from Windows.
            int lastSeparator = destination.LastIndexOfAny(['\\', '/']);
            string name = destination[(lastSeparator + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(name)) name = "Untitled file";
            string url = Text(item, "url");
            string host = Uri.TryCreate(url, UriKind.Absolute, out Uri? address) &&
                          address.Scheme is "http" or "https"
                ? address.IdnHost
                : "Unknown source";
            string state = Text(item, "state");
            if (active is not null &&
                (state is "Running" or "SuspendRequested" or "CancelRequested") &&
                !active.Contains(id))
            {
                // A process crash can leave a durable Running marker even though
                // its network worker no longer exists. Resuming reuses native
                // partial-file validation; never show a fake live transfer.
                state = "Interrupted";
            }
            // Invalid entries are retained as unknown status rather than treated
            // as runnable queue operations.
            if (state.Length == 0) continue;
            string label = state switch
            {
                "Pending" => "Queued",
                "Running" => "Downloading",
                "SuspendRequested" => "Pausing",
                "Suspended" => "Paused",
                "Interrupted" => "Interrupted — ready to resume",
                "CancelRequested" => "Cancelling",
                "Cancelled" => "Cancelled",
                "Completed" => "Completed",
                "Failed" => "Failed",
                _ => "Unknown state",
            };
            long completeBytes = Math.Max(0, Number(item, "bytes"));
            long partialBytes = Math.Max(0, Number(item, "partialBytes"));
            long expectedBytes = Math.Max(0, Number(item, "expectedBytes"));
            long transferred = state == "Completed" ? completeBytes : partialBytes;
            bool isRunning = state is "Running" or "SuspendRequested" or "CancelRequested";
            bool hasKnownTotal = expectedBytes > 0;
            double percent = hasKnownTotal
                ? Math.Clamp((double)transferred / expectedBytes * 100d, 0d, 100d)
                : state == "Completed" ? 100d : 0d;
            string size = hasKnownTotal && state != "Completed"
                ? $"{Size(transferred)} of {Size(expectedBytes)}"
                : transferred > 0 ? Size(transferred) : "No data yet";
            bool showProgress = state is "Running" or "SuspendRequested" or "Suspended" or "Completed";
            DateTimeOffset created = DateTimeOffset.MinValue;
            if (item.TryGetProperty("createdAt", out JsonElement date) &&
                date.ValueKind == JsonValueKind.String &&
                date.TryGetDateTimeOffset(out DateTimeOffset parsed))
                created = parsed;
            jobs.Add(new DownloadJobRow(
                id, name, host, destination, state, label, size, percent,
                isRunning && !hasKnownTotal, showProgress,
                state is "Pending" or "Failed",
                state == "Running",
                state is "Suspended" or "Interrupted",
                state is "Pending" or "Running" or "Suspended" or "Interrupted",
                state is "Completed" or "Cancelled" or "Failed",
                created));
        }
        return jobs
            .OrderByDescending(job => job.CreatedAt)
            .ThenBy(job => job.Id, StringComparer.Ordinal)
            .Take(MaxVisibleJobs)
            .ToArray();
    }

    public static string Size(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double size = bytes;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return size.ToString("0.#", CultureInfo.CurrentCulture) + " " + units[unit];
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static long Number(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number)
            ? number : 0;
}


/// <summary>Simple, privacy-safe view filtering over already-redacted job rows.</summary>
public static class DownloadViewFilters
{
    public static IReadOnlyList<DownloadJobRow> Apply(IEnumerable<DownloadJobRow> jobs, string filter, string? search)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        IEnumerable<DownloadJobRow> items = filter switch
        {
            "Active" => jobs.Where(j => j.State is "Pending" or "Running" or "SuspendRequested" or "Suspended" or "Interrupted" or "CancelRequested"),
            "Finished" => jobs.Where(j => j.State is "Completed" or "Cancelled" or "Failed"),
            _ => jobs,
        };
        string query = search?.Trim() ?? string.Empty;
        if (query.Length > 0)
            items = items.Where(job =>
                job.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                job.Host.Contains(query, StringComparison.OrdinalIgnoreCase));
        return items.ToArray();
    }
}
