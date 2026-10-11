using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using WinCare.Application.Storage;

namespace WinCare.Application.Diagnostics;

/// <summary>Per-process readings, not a claim about total system memory usage.</summary>
public sealed record MonitoredProcess(
    int Id, string Name, long? WorkingSetBytes, double? CpuPercent, DateTimeOffset? StartedAt)
{
    public string CpuLabel => CpuPercent is double value ? $"{value.ToString("F1", CultureInfo.CurrentCulture)}%" : "—";
    public string MemoryLabel => WorkingSetBytes is long bytes && bytes >= 0
        ? StorageExplorer.FormatBytes((ulong)bytes) : "Unknown";
    public bool CanEnd => ProcessMonitorService.CanEnd(this);
}

public sealed record ProcessSnapshot(IReadOnlyList<MonitoredProcess> Items, int TotalCount, bool Truncated);

public sealed record EndTaskResult(bool Succeeded, string Message);

/// <summary>
/// Native, local process reader. No WMI, kernel hooks, background agent, or elevation.
/// Memory is resident working set and CPU requires consecutive valid samples.
/// </summary>
public sealed class ProcessMonitorService
{
    private Dictionary<int, PreviousSample> _previous = [];
    private DateTimeOffset? _previousTime;

    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "Secure System",
        "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsm",
        "svchost", "sihost", "dwm", "explorer"
    };

    /// <summary>Read up to 1,000 task rows without opening other processes for mutation.</summary>
    public ProcessSnapshot Capture(CancellationToken cancellationToken = default)
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        TimeSpan elapsed = _previousTime.HasValue ? timestamp - _previousTime.Value : TimeSpan.Zero;
        var next = new Dictionary<int, PreviousSample>();
        var result = new List<MonitoredProcess>();
        using var processSet = new ProcessSet();
        foreach (Process process in processSet.Processes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int pid;
            string name;
            try { pid = process.Id; name = process.ProcessName; }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { continue; }

            long? memory = Read(() => process.WorkingSet64);
            TimeSpan? cpuTime = Read(() => process.TotalProcessorTime);
            DateTimeOffset? started = Read(() => new DateTimeOffset(process.StartTime.ToUniversalTime()));
            double? cpu = null;
            if (cpuTime.HasValue && _previous.TryGetValue(pid, out PreviousSample previous) &&
                previous.StartedAt == started && elapsed > TimeSpan.Zero)
            {
                cpu = CalculateCpuPercent(previous.CpuTime, cpuTime, elapsed, Environment.ProcessorCount);
            }
            next[pid] = new PreviousSample(cpuTime, started);
            result.Add(new MonitoredProcess(pid, name, memory, cpu, started));
        }
        _previous = next;
        _previousTime = timestamp;
        var ordered = result.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id).ToArray();
        return new ProcessSnapshot(ordered.Take(1000).ToArray(), ordered.Length, ordered.Length > 1000);
    }

    /// <summary>Percentage of available logical-processor capacity, not one-core percentage.</summary>
    public static double? CalculateCpuPercent(TimeSpan? before, TimeSpan? after, TimeSpan elapsed, int logicalProcessors)
    {
        if (!before.HasValue || !after.HasValue || elapsed <= TimeSpan.Zero || logicalProcessors <= 0 ||
            after.Value < before.Value) return null;
        return Math.Clamp((after.Value - before.Value).TotalMilliseconds /
            (elapsed.TotalMilliseconds * logicalProcessors) * 100d, 0, 100);
    }

    public static bool CanEnd(MonitoredProcess item) =>
        item.Id > 4 && item.Id != Environment.ProcessId && item.StartedAt.HasValue &&
        !ProtectedNames.Contains(item.Name);

    /// <summary>
    /// End exactly one verified process after the UI receives an explicit confirmation.
    /// Refuses stale IDs, critical names, the host process, and inaccessible process identity.
    /// </summary>
    public static EndTaskResult EndTask(MonitoredProcess selected)
    {
        if (!CanEnd(selected)) return new(false, "WinCare cannot end this system or unidentified process.");
        try
        {
            using Process process = Process.GetProcessById(selected.Id);
            DateTimeOffset liveStart = new(process.StartTime.ToUniversalTime());
            if (!string.Equals(process.ProcessName, selected.Name, StringComparison.OrdinalIgnoreCase) ||
                liveStart != selected.StartedAt)
            {
                return new(false, "The selected process changed or restarted. Refresh before trying again.");
            }
            process.Kill(entireProcessTree: false);
            return process.WaitForExit(2000)
                ? new(true, $"{selected.Name} ({selected.Id}) has exited.")
                : new(false, "Windows received the end request, but this task is still closing.");
        }
        catch (ArgumentException)
        {
            return new(false, "The process already exited.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return new(false, $"Windows could not end this process: {ex.Message}");
        }
    }

    private static T? Read<T>(Func<T> read) where T : struct
    {
        try { return read(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        { return null; }
    }

    private sealed record PreviousSample(TimeSpan? CpuTime, DateTimeOffset? StartedAt);

    private sealed class ProcessSet : IDisposable
    {
        public Process[] Processes { get; } = Process.GetProcesses();
        public void Dispose()
        {
            foreach (Process process in Processes) process.Dispose();
        }
    }
}
